using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Keeps a bounded set of browsers alive, hands them out one at a time and closes the idle ones.
/// </summary>
/// <remarks>
/// Ownership of a slot travels through a channel rather than through locks: a slot is either in the channel and
/// free, or held by a lease, or held by the reaper, and never two of those at once. That is what makes idle
/// disposal safe without the cancellation-token juggling an earlier implementation used, where a launch and a
/// shutdown could race for the same browser and the idle delay came out as
/// <c>RetryDelay + 5s + BrowserIdleTimeout - 1s</c> rather than the configured timeout.
/// </remarks>
public sealed class BrowserPool : IBrowserPool
{
    private readonly ScrapingOptions _options;
    private readonly ILogger<BrowserPool> _logger;
    private readonly Slot[] _slots;
    private readonly Channel<Slot> _free;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _reaper;
    private int _isDisposed;

    /// <summary>The Playwright driver, started once however many callers arrive together.</summary>
    /// <remarks>
    /// Was a null check, a semaphore and a second null check — double-checked locking with no barrier on the
    /// unguarded read, which the pattern requires and which this did not have. What it buys is not a crash but
    /// a second driver: another node process, started and then forgotten, for the whole life of the pool.
    /// <see cref="Lazy{T}"/> runs its factory exactly once and publishes it safely by default, which is the
    /// whole of what the semaphore was there for.
    /// </remarks>
    private readonly Lazy<Task<IPlaywright>> _driver = new(Playwright.CreateAsync);

    /// <summary>Creates a pool sized by <see cref="ScrapingOptions.MaxBrowsers"/>.</summary>
    public BrowserPool(IOptions<ScrapingOptions> options, ILogger<BrowserPool> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
        _slots = [.. Enumerable.Range(0, Math.Max(1, _options.MaxBrowsers)).Select(index => new Slot(index))];
        _free = Channel.CreateUnbounded<Slot>();

        foreach (var slot in _slots)
            _free.Writer.TryWrite(slot);

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
    /// thing. The guard alone answered only the first of them: the second waited on a channel nobody would
    /// ever write to again, which is how a shutdown with work in flight used to hang instead of ending.
    /// </remarks>
    public async Task<IBrowserLease> RentAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) == 1, this);

        var slot = await QueuedAsync(ct);

        try
        {
            await EnsureLiveAsync(slot, ct);

            return new Lease(this, slot);
        }
        catch
        {
            _free.Writer.TryWrite(slot);
            throw;
        }
    }

    private async Task<Slot> QueuedAsync(CancellationToken ct)
    {
        try
        {
            return await _free.Reader.ReadAsync(ct);
        }
        catch (ChannelClosedException)
        {
            throw new ObjectDisposedException(nameof(BrowserPool));
        }
    }

    private async Task EnsureLiveAsync(Slot slot, CancellationToken ct)
    {
        if (slot.Browser is { IsConnected: true })
            return;

        if (slot.Browser is not null)
        {
            _logger.LogWarning("Browser {Slot} lost its connection and is being replaced", slot.Index);
            await CloseAsync(slot);
        }

        var chosen = Chosen(await _driver.Value.WaitAsync(ct));

        slot.Browser = IsRemote
            ? await chosen.ConnectAsync(_options.BrowserServer!, new BrowserTypeConnectOptions
            {
                Timeout = _options.NavigationTimeoutMs
            })
            : await chosen.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _options.Headless,
                Args = _options.Engine is Engine.Chromium ? ChromiumArguments.For(_options) : null
            });

        _logger.LogInformation(
            "Browser {Slot} is ready: {Engine}, {Mode}",
            slot.Index, _options.Engine, IsRemote ? "connected" : "launched");
    }

    private IBrowserType Chosen(IPlaywright driver) => _options.Engine switch
    {
        Engine.Firefox => driver.Firefox,
        Engine.WebKit => driver.Webkit,
        _ => driver.Chromium
    };

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

    private async Task SweepOnceAsync()
    {
        for (var visited = 0; visited < _slots.Length; visited++)
        {
            if (!_free.Reader.TryRead(out var slot))
                return;

            try
            {
                if (slot.Browser is not null && DateTimeOffset.UtcNow - slot.ReturnedAt > _options.BrowserIdleTimeout)
                {
                    _logger.LogInformation("Browser {Slot} was idle for {Timeout} and is being closed",
                        slot.Index, _options.BrowserIdleTimeout);
                    await CloseAsync(slot);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Browser {Slot} could not be closed while idle", slot.Index);
            }
            finally
            {
                _free.Writer.TryWrite(slot);
            }
        }
    }

    private async Task CloseAsync(Slot slot)
    {
        var browser = slot.Browser;
        slot.Browser = null;

        if (browser is null)
            return;

        try
        {
            await browser.CloseAsync();
        }
        catch (PlaywrightException exception)
        {
            _logger.LogDebug(exception, "Browser {Slot} refused to close cleanly", slot.Index);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    private void Return(Slot slot)
    {
        slot.ReturnedAt = DateTimeOffset.UtcNow;
        _free.Writer.TryWrite(slot);
    }

    private static TimeSpan Sweep(TimeSpan idleTimeout)
    {
        var half = idleTimeout / 2;

        return half < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : half;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every step here is something that can fail, and none of them may take the rest down with it. Closing
    /// the free channel is first because it is what releases a caller already queued for a browser; the reaper
    /// is awaited for its ending rather than its answer; and the driver and the tokens are released in a
    /// <c>finally</c>, because one browser that refuses to close would otherwise leak the process that hosts
    /// them all.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            return;

        _free.Writer.TryComplete();

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
            foreach (var slot in _slots)
                await CloseAsync(slot);
        }
        finally
        {
            if (_driver.IsValueCreated && _driver.Value.IsCompletedSuccessfully)
                (await _driver.Value).Dispose();

            _shutdown.Dispose();
        }
    }

    private sealed class Slot(int index)
    {
        public int Index { get; } = index;

        public IBrowser? Browser { get; set; }

        public DateTimeOffset ReturnedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class Lease(BrowserPool pool, Slot slot) : IBrowserLease
    {
        private int _isReturned;

        public IBrowser Browser { get; } = slot.Browser!;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isReturned, 1) == 0)
                pool.Return(slot);

            return ValueTask.CompletedTask;
        }
    }
}
