using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Holds the Playwright installation and opens browsers from it, launched here or connected to elsewhere.
/// </summary>
/// <remarks>
/// The installation is started once and kept, because unpacking it is the expensive part and every browser
/// after the first comes from the same one. A start that FAILED is not kept, and that distinction is the whole
/// of this class: a <c>Lazy&lt;Task&lt;IPlaywright&gt;&gt;</c> caches the task, so one failure — a machine busy
/// while the driver unpacks, a temporary file that was not there yet — answered every later rent with the same
/// exception until the process restarted. It was named in the code as undemonstrable on 2026-09-15, since
/// <see cref="Playwright.CreateAsync"/> is static and no test can make it fail. Asking for it through a
/// function rather than by name is what made it demonstrable, and the test that follows failed before this was
/// written.
/// </remarks>
internal sealed class Driver(ScrapingOptions options, Func<Task<IPlaywright>>? start = null) : IDisposable
{
    private readonly Func<Task<IPlaywright>> _start = start ?? Playwright.CreateAsync;
    private readonly SemaphoreSlim _starting = new(1, 1);

    private Task<IPlaywright>? _started;

    internal bool IsRemote => !string.IsNullOrWhiteSpace(options.BrowserServer);

    internal async Task<IBrowser> OpenedAsync(CancellationToken ct)
    {
        var started = await StartedAsync(ct);

        var chosen = Chosen(started);

        return IsRemote
            ? await chosen.ConnectAsync(options.BrowserServer!, new BrowserTypeConnectOptions
            {
                Timeout = options.NavigationTimeoutMs
            })
            : await chosen.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = options.Headless,
                Args = options.Engine is Engine.Chromium ? ChromiumArguments.For(options) : null
            });
    }

    /// <summary>The installation, started on the first ask and kept for every ask after it.</summary>
    /// <remarks>
    /// A start already under way is joined rather than repeated, which is what the lock is for. A start that
    /// faulted is forgotten, so the next caller tries again; a caller that gave up waiting is not a failed
    /// start, and the installation it left running is kept for whoever asks next.
    /// </remarks>
    private async Task<IPlaywright> StartedAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _started) is { } running)
            return await running.WaitAsync(ct);

        await _starting.WaitAsync(ct);

        try
        {
            if (Volatile.Read(ref _started) is { } known)
                return await known.WaitAsync(ct);

            var starting = _start();

            try
            {
                var driver = await starting.WaitAsync(ct);

                Volatile.Write(ref _started, starting);

                return driver;
            }
            catch when (!starting.IsFaulted)
            {
                Volatile.Write(ref _started, starting);

                throw;
            }
        }
        finally
        {
            _starting.Release();
        }
    }

    private IBrowserType Chosen(IPlaywright driver) => options.Engine switch
    {
        Engine.Firefox => driver.Firefox,
        Engine.WebKit => driver.Webkit,
        _ => driver.Chromium
    };

    public void Dispose()
    {
        if (Volatile.Read(ref _started) is { IsCompletedSuccessfully: true } started)
            started.Result.Dispose();

        _starting.Dispose();
    }
}
