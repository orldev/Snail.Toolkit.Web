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
/// </remarks>
public sealed class BrowserPool : IBrowserPool
{
    private readonly ScrapingOptions _options;
    private readonly ILogger<BrowserPool> _logger;
    private readonly int _ceiling;
    private readonly SemaphoreSlim _pages;
    private readonly SemaphoreSlim _opening = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _reaper;
    private IBrowser? _browser;
    private long _idleSince = DateTimeOffset.UtcNow.UtcTicks;
    private int _isDisposed;

    /// <summary>Creates a pool that allows <see cref="ScrapingOptions.MaxDegreeOfParallelism"/> pages at once.</summary>
    public BrowserPool(IOptions<ScrapingOptions> options, ILogger<BrowserPool> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
        _ceiling = Math.Max(1, _options.MaxDegreeOfParallelism);
        _pages = new SemaphoreSlim(_ceiling, _ceiling);

        if (IsRemote && _options.BrowserArguments.Count > 0)
            _logger.LogWarning(
                "{Count} browser arguments are configured but a browser server is in use, and a connection "
                + "carries no command line: the server was started with its own. Measured consequence: with "
                + "'--disable-blink-features=AutomationControlled' among them, a connected browser still reports "
                + "navigator.webdriver as true. Pass the arguments to the server's own launch command instead.",
                _options.BrowserArguments.Count);

        _reaper = IsRemote ? Task.CompletedTask : Task.Run(() => ReapIdleAsync(_shutdown.Token));
    }

    private bool IsRemote => !string.IsNullOrWhiteSpace(_options.BrowserServer);

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
            Return();
            throw;
        }
    }

    private async Task QueuedAsync(CancellationToken ct)
    {
        try
        {
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);

            await _pages.WaitAsync(waiting.Token);
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
                _logger.LogWarning("The browser lost its connection and is being replaced");
                await CloseAsync();
            }

            var opened = await Opened(opening.Token);

            // The pool can close while this is still opening, and then CloseAsync has already read the field
            // and written null. Storing the browser here would put it somewhere nothing looks again: the
            // process it launched, or the connection it holds, outlives the pool that made it.
            if (Volatile.Read(ref _isDisposed) == 1)
            {
                await Closed(opened);

                throw new ObjectDisposedException(nameof(BrowserPool));
            }

            Volatile.Write(ref _browser, opened);

            _logger.LogInformation(
                "Browser is ready: {Engine}, {Mode}, up to {Pages} page(s) at once",
                _options.Engine, IsRemote ? "connected" : "launched", _ceiling);

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
    private async Task<IBrowser> Opened(CancellationToken ct)
    {
        var opening = OpenedAsync(ct);

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
    private void Abandon(Task<IBrowser> opening)
        => _ = opening.ContinueWith(
            ended =>
            {
                if (ended.IsCompletedSuccessfully)
                    return Closed(ended.Result);

                _ = ended.Exception;

                return Task.CompletedTask;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private async Task<IBrowser> OpenedAsync(CancellationToken ct)
    {
        var driver = await _driver.Value.WaitAsync(ct);

        var chosen = Chosen(driver);

        return IsRemote
            ? await chosen.ConnectAsync(_options.BrowserServer!, new BrowserTypeConnectOptions
            {
                Timeout = _options.NavigationTimeoutMs
            })
            : await chosen.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _options.Headless,
                Args = _options.Engine is Engine.Chromium ? ChromiumArguments.For(_options) : null
            });
    }

    private IBrowserType Chosen(IPlaywright driver) => _options.Engine switch
    {
        Engine.Firefox => driver.Firefox,
        Engine.WebKit => driver.Webkit,
        _ => driver.Chromium
    };

    /// <summary>The Playwright driver, started once however many callers arrive together.</summary>
    /// <remarks>
    /// Was a null check, a semaphore and a second null check — double-checked locking with no barrier on the
    /// unguarded read, which the pattern requires and which this did not have. What it buys is not a crash but
    /// a second driver: another node process, started and then forgotten, for the whole life of the pool.
    /// <see cref="Lazy{T}"/> runs its factory exactly once and publishes it safely by default, which is the
    /// whole of what the semaphore was there for.
    /// <para>
    /// KNOWN AND NOT FIXED, so that it is not rediscovered as news: exactly once includes once unsuccessfully.
    /// The factory returns a <see cref="Task{TResult}"/> rather than throwing, so a driver that fails to start
    /// is a faulted task cached for the life of the pool, and every later <see cref="RentAsync"/> awaits that
    /// same fault — one transient failure at start-up is a pool that never works again. It is left alone
    /// because nothing here can demonstrate it: <see cref="Playwright.CreateAsync"/> is static, so no test in
    /// this suite can make it fail, and a retry written against a failure nobody can produce is a guess about
    /// the hot path rather than a fix. Making it testable means giving the pool a driver factory to be handed,
    /// which is the change to weigh — not a retry bolted on here.
    /// </para>
    /// </remarks>
    private readonly Lazy<Task<IPlaywright>> _driver = new(Playwright.CreateAsync);

    private async Task ReapIdleAsync(CancellationToken ct)
    {
        if (_options.BrowserIdleTimeout <= TimeSpan.Zero)
            return;

        using var timer = new PeriodicTimer(Sweep(_options.BrowserIdleTimeout));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SweepOnceAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Closes the browser when nothing has used it for a while.</summary>
    /// <remarks>
    /// Every page's permission is taken before the browser is closed, and the sweep gives up if it cannot have
    /// them all. That is what makes closing safe without any cancellation juggling: a caller is either holding
    /// a permit, in which case the sweep does nothing, or it is not, in which case it cannot be inside.
    /// </remarks>
    private async Task SweepOnceAsync()
    {
        if (Volatile.Read(ref _browser) is null)
            return;

        if (DateTimeOffset.UtcNow.UtcTicks - Interlocked.Read(ref _idleSince) <= _options.BrowserIdleTimeout.Ticks)
            return;

        if (!Emptied())
            return;

        try
        {
            _logger.LogInformation("The browser was idle for {Timeout} and is being closed", _options.BrowserIdleTimeout);

            await CloseAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The idle browser could not be closed");
        }
        finally
        {
            _pages.Release(_ceiling);
        }
    }

    private bool Emptied()
    {
        var taken = 0;

        while (taken < _ceiling && _pages.Wait(0))
            taken++;

        if (taken == _ceiling)
            return true;

        if (taken > 0)
            _pages.Release(taken);

        return false;
    }

    private async Task CloseAsync()
    {
        var browser = Volatile.Read(ref _browser);

        Volatile.Write(ref _browser, null);

        if (browser is not null)
            await Closed(browser);
    }

    private async Task Closed(IBrowser browser)
    {
        try
        {
            await browser.CloseAsync();
        }
        catch (PlaywrightException exception)
        {
            _logger.LogDebug(exception, "The browser refused to close cleanly");
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    private void Return()
    {
        Interlocked.Exchange(ref _idleSince, DateTimeOffset.UtcNow.UtcTicks);
        _pages.Release();
    }

    private static TimeSpan Sweep(TimeSpan idleTimeout)
    {
        var half = idleTimeout / 2;

        return half < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : half;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every step here is something that can fail, and none of them may take the rest down with it. The
    /// shutdown token is cancelled first because it is what releases a caller already queued for a page; the
    /// reaper is awaited for its ending rather than its answer; and the driver and the tokens are released in a
    /// <c>finally</c>, because a browser that refuses to close would otherwise leak the process that hosts it.
    /// <para>
    /// NEITHER semaphore is disposed, for the reason their counterpart in the politeness gate is not: a
    /// caller released after shutdown would meet a disposed object, and nothing here holds a handle worth
    /// reclaiming. That was true of the page semaphore from the start and was not extended to the opening
    /// one, which cost more than an unhelpful exception. <see cref="SemaphoreSlim.Dispose()"/> does not wake
    /// anybody waiting on it — a caller queued behind an opening browser was left on a gate nobody would
    /// open again and never returned at all, which is word for word the failure the page semaphore already
    /// had a paragraph about. MEASURED on 2026-09-15: the test host had to be killed.
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
            _logger.LogError(exception, "The idle reaper ended badly; the pool is closing anyway");
        }

        try
        {
            await CloseAsync();
        }
        finally
        {
            if (_driver.IsValueCreated && _driver.Value.IsCompletedSuccessfully)
                (await _driver.Value).Dispose();

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
                pool.Return();

            return ValueTask.CompletedTask;
        }
    }
}
