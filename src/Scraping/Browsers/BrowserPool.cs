using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Keeps one browser alive and hands out permission to have a page open on it.
/// </summary>
/// <remarks>
/// A lease used to be a whole browser process, held for the length of a visit, so the number of pages in flight
/// could never exceed the number of processes the pool was willing to run — one, at the defaults. A lease is
/// now one page's worth of permission on a shared browser, and the ceiling is how many pages may be open at
/// once rather than how many processes may exist.
/// <para>
/// MEASURED on 2026-09-14 by <c>probes/contexts</c>, which is the whole reason this is shaped this way. On
/// stock Playwright a browser process with one page costs 284 MB and every further context on it costs 70 —
/// four times cheaper — and opening one takes 133 ms against 340 for a process, with nothing degrading up to
/// sixteen. So processes buy nothing a context does not buy cheaper.
/// </para>
/// <para>
/// The same probe answered something that removed the choice altogether: against our own browser image the
/// count of browser processes in the container stays at ONE however many connections are made, because that
/// image publishes an already-launched browser. A pool that counted connections was calling them browsers and
/// getting one either way. Keeping several was therefore not a trade between isolation and memory — it was a
/// setting with no effect, and it is gone.
/// </para>
/// <para>
/// What is given up is stated plainly: a browser that crashes takes every page in flight with it, where
/// several processes would have cost a fraction. Against a browser server that cannot be bought back at any
/// price, and the retry in <see cref="IAttempts{TValue}"/> is what answers it.
/// </para>
/// <para>
/// WHAT WAS TAKEN OUT OF HERE ON 2026-09-15, and what deliberately was not. This file was 439 lines and had
/// been the source of a defect three times over. Three of its four jobs came out cleanly and are now
/// <see cref="Driver"/> (start Playwright, open a browser of the configured engine), <see cref="Permits"/>
/// (how many pages at once, and the idle clock) and <see cref="Reaper"/> (close a browser nobody is using).
/// Two of the three historical defects lived in what is now the first and second of those.
/// </para>
/// <para>
/// THE FOURTH — opening the browser — WAS PROPOSED AND REFUSED, and the reason is written here because it is
/// the obvious next thing to suggest about a class that is still the longest in the package. It reads as a
/// separable job and is not one: what makes it correct is the two things it shares with shutdown, and both
/// are things a second object could only be handed a copy of. The gate it waits on is linked to
/// <c>_shutdown</c>, which also releases callers queued in <see cref="QueuedAsync"/> for a page — one
/// cancellation serving two gates, and the remark on <see cref="OpeningAsync"/> says outright that the two
/// are written as a pair and for the same reason. And what decides whether a browser that has finished
/// opening is published or closed is <c>_isDisposed</c>, read at the one instant that races shutdown; handed
/// across an object boundary it becomes two facts that have to be kept in step by whoever remembers to,
/// which is the exact shape of the defect that cost a hung test host.
/// </para>
/// <para>
/// There is a third, smaller reason and it is the one this library cares about most: all three refusals a
/// caller can meet in there name <c>BrowserPool</c>. Moved, they would name a type the caller has never
/// heard of — which is word for word the failure the remark on <see cref="OpeningAsync"/> exists to prevent.
/// </para>
/// </remarks>
public sealed class BrowserPool : IBrowserPool
{
    private readonly ScrapingOptions _options;
    private readonly ILogger<BrowserPool> _logger;
    private readonly Driver _driver;
    private readonly Permits _permits;
    [SuppressMessage("Reliability", "CA2213", Justification = "Left undisposed on purpose, see DisposeAsync: a caller released after shutdown would meet a disposed gate, and disposing it wakes nobody waiting on it.")]
    private readonly SemaphoreSlim _opening = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _reaper;
    private IBrowser? _browser;
    private int _isDisposed;

    /// <summary>Creates a pool that allows <see cref="ScrapingOptions.MaxDegreeOfParallelism"/> pages at once.</summary>
    public BrowserPool(IOptions<ScrapingOptions> options, ILogger<BrowserPool> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
        _driver = new Driver(_options);
        _permits = new Permits(Math.Max(1, _options.MaxDegreeOfParallelism));

        if (_driver.IsRemote && _options.BrowserArguments.Count > 0)
            Log.ArgumentsIgnored(_logger, _options.BrowserArguments.Count);

        _reaper = _driver.IsRemote
            ? Task.CompletedTask
            : Task.Run(() => new Reaper(
                _options.BrowserIdleTimeout,
                _permits,
                () => Volatile.Read(ref _browser) is not null,
                CloseAsync,
                _logger).ReapAsync(_shutdown.Token));
    }

    /// <inheritdoc />
    /// <remarks>
    /// A caller that arrives after disposal and one that was already queued when it began are told the same
    /// thing. The guard alone answered only the first of them: the second waited on a gate nobody would ever
    /// open again, which is how a shutdown with work in flight used to hang instead of ending.
    /// </remarks>
    public async Task<IBrowserLease> RentAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) == 1, this);

        await QueuedAsync(ct);

        try
        {
            return new Lease(this, await LiveAsync(ct));
        }
        catch
        {
            _permits.Return();
            throw;
        }
    }

    private async Task QueuedAsync(CancellationToken ct)
    {
        try
        {
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);

            await _permits.WaitAsync(waiting.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
                                          && !ct.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(BrowserPool));
        }
    }

    /// <summary>The browser, opened on first use and replaced when its connection dies.</summary>
    /// <remarks>
    /// The unguarded read is deliberate and volatile: the common case is a live browser and many callers a
    /// second, and making every one of them take a semaphore to learn what is already true would serialise the
    /// hot path for the sake of the rare one. The check is repeated inside, so two callers arriving on a dead
    /// browser open one between them rather than one each.
    /// </remarks>
    private async Task<IBrowser> LiveAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _browser) is { IsConnected: true } live)
            return live;

        try
        {
            return await OpeningAsync(ct);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
                                          && !ct.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(BrowserPool));
        }
    }

    /// <summary>Opens the browser under one caller, with the shutdown able to call the wait off.</summary>
    /// <remarks>
    /// Every way this can end while the pool is closing is answered by its caller in one place, which is the
    /// shape <see cref="QueuedAsync"/> already uses and for the same reason. There are three of them and the
    /// third is easy to miss: the wait can be cancelled, the semaphore can be disposed, and reading
    /// <see cref="CancellationTokenSource.Token"/> off a <see cref="CancellationTokenSource"/> that has been
    /// disposed throws as well — so the linked source has to be built inside the guard rather than beside it,
    /// or a caller arriving late is told a <c>CancellationTokenSource</c> was disposed and left to guess whose.
    /// <para>
    /// The pool can close while this is still opening, and then CloseAsync has already read the field and written
    /// null. Storing the browser here would put it somewhere nothing looks again: the process it launched, or the
    /// connection it holds, outlives the pool that made it.
    /// </para>
    /// </remarks>
    private async Task<IBrowser> OpeningAsync(CancellationToken ct)
    {
        using var opening = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);

        await _opening.WaitAsync(opening.Token);

        try
        {
            if (Volatile.Read(ref _browser) is { IsConnected: true } known)
                return known;

            if (_browser is not null)
            {
                Log.ConnectionLost(_logger);
                await CloseAsync();
            }

            var opened = await OpenedAsync(opening.Token);

            if (Volatile.Read(ref _isDisposed) == 1)
            {
                await ClosedAsync(opened);

                throw new ObjectDisposedException(nameof(BrowserPool));
            }

            Volatile.Write(ref _browser, opened);

            Log.BrowserReady(_logger, _options.Engine, _driver.IsRemote ? "connected" : "launched", _permits.Ceiling);

            return opened;
        }
        finally
        {
            _opening.Release();
        }
    }

    /// <summary>The browser, or release from waiting for one that cannot be called off.</summary>
    /// <remarks>
    /// Playwright's <c>ConnectAsync</c> and <c>LaunchAsync</c> take a timeout and no cancellation token, so a
    /// browser that has begun opening cannot be told to stop. Waiting for it anyway is what made shutdown
    /// wait out the whole connect timeout — forty-five seconds at the defaults, against a pool that had
    /// already closed.
    /// <para>
    /// The caller is therefore released at once and the attempt is left to finish on its own, where the
    /// browser it produces is closed rather than abandoned. That is the difference between a connect nobody
    /// is waiting for and a browser process nobody owns.
    /// </para>
    /// </remarks>
    private async Task<IBrowser> OpenedAsync(CancellationToken ct)
    {
        var opening = _driver.OpenedAsync(ct);

        try
        {
            return await opening.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Abandon(opening);

            throw;
        }
    }

    /// <summary>Closes a browser that finished opening after everybody stopped waiting for it.</summary>
    private void Abandon(Task<IBrowser> opening) => _ = ClosedOnceOpenAsync(opening);

    /// <summary>Waits for an abandoned opening to end, and closes the browser when it opened at all.</summary>
    /// <remarks>An opening that failed is observed and dropped: nobody is left to be told.</remarks>
    private async Task ClosedOnceOpenAsync(Task<IBrowser> opening)
    {
        await ((Task)opening).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (!opening.IsCompletedSuccessfully)
        {
            _ = opening.Exception;

            return;
        }

        await ClosedAsync(await opening);
    }

    private async Task CloseAsync()
    {
        var browser = Volatile.Read(ref _browser);

        Volatile.Write(ref _browser, null);

        if (browser is not null)
            await ClosedAsync(browser);
    }

    private async Task ClosedAsync(IBrowser browser)
    {
        try
        {
            await browser.CloseAsync();
        }
        catch (PlaywrightException exception)
        {
            Log.CloseRefused(_logger, exception);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every step here is something that can fail, and none of them may take the rest down with it. The
    /// shutdown token is cancelled first because it is what releases a caller already queued for a page; the
    /// reaper is awaited for its ending rather than its answer; and the driver and the tokens are released in a
    /// <c>finally</c>, because a browser that refuses to close would otherwise leak the process that hosts it.
    /// <para>
    /// NEITHER GATE IS DISPOSED — not <see cref="_opening"/> here, and not the one inside
    /// <see cref="Permits"/>, which is why that type has no <c>Dispose</c> to call. A caller released after
    /// shutdown would meet a disposed object, and nothing here holds a handle worth reclaiming:
    /// <see cref="SemaphoreSlim"/> allocates one only for <c>AvailableWaitHandle</c>, which nobody asks for.
    /// <see cref="SemaphoreSlim.Dispose()"/> does not wake anybody waiting on it either — a caller queued
    /// behind an opening browser was left on a gate nobody would open again and never returned at all.
    /// MEASURED on 2026-09-15: the test host had to be killed.
    /// </para>
    /// <para>
    /// The rule held for the page semaphore from the start and was not extended to this one, which is exactly
    /// the kind of omission a paragraph cannot prevent and a type can. Half of it is now enforced by
    /// <see cref="Permits"/> having no way to be disposed; this field is the half still kept by hand, and it
    /// is kept here rather than moved because what it guards — publishing the browser — is what shutdown
    /// races with.
    /// </para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            return;

        await _shutdown.CancelAsync();

        try
        {
            await _reaper;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Log.ReaperFailed(_logger, exception);
        }

        try
        {
            await CloseAsync();
        }
        finally
        {
            _driver.Dispose();

            _shutdown.Dispose();
        }
    }

    private sealed class Lease(BrowserPool pool, IBrowser browser) : IBrowserLease
    {
        private int _isReturned;

        public IBrowser Browser { get; } = browser;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isReturned, 1) == 0)
                pool._permits.Return();

            return ValueTask.CompletedTask;
        }
    }
}
