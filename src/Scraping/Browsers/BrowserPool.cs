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
    private readonly SemaphoreSlim _driverGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _reaper;
    private IPlaywright? _driver;
    private int _isDisposed;

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

        _reaper = IsRemote ? Task.CompletedTask : Task.Run(() => ReapIdleAsync(_shutdown.Token));
    }

    private bool IsRemote => !string.IsNullOrWhiteSpace(_options.BrowserServer);

    /// <inheritdoc />
    public async Task<IBrowserLease> RentAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) == 1, this);

        var slot = await _free.Reader.ReadAsync(ct);

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

    private async Task EnsureLiveAsync(Slot slot, CancellationToken ct)
    {
        if (slot.Browser is { IsConnected: true })
            return;

        if (slot.Browser is not null)
        {
            _logger.LogWarning("Browser {Slot} lost its connection and is being replaced", slot.Index);
            await CloseAsync(slot);
        }

        var driver = await DriverAsync(ct);

        slot.Browser = IsRemote
            ? await driver.Chromium.ConnectAsync(_options.BrowserServer!, new BrowserTypeConnectOptions
            {
                Timeout = _options.NavigationTimeoutMs
            })
            : await driver.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _options.Headless,
                Args = ChromiumArguments.For(_options)
            });

        _logger.LogInformation("Browser {Slot} is ready ({Mode})", slot.Index, IsRemote ? "connected" : "launched");
    }

    private async Task<IPlaywright> DriverAsync(CancellationToken ct)
    {
        if (_driver is not null)
            return _driver;

        await _driverGate.WaitAsync(ct);

        try
        {
            return _driver ??= await Playwright.CreateAsync();
        }
        finally
        {
            _driverGate.Release();
        }
    }

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

        foreach (var slot in _slots)
            await CloseAsync(slot);

        _driver?.Dispose();
        _driver = null;
        _driverGate.Dispose();
        _shutdown.Dispose();
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
