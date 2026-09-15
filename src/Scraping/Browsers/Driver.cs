using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Starts Playwright once and opens browsers of the configured engine, connected or launched.
/// </summary>
/// <remarks>
/// This is the only part of the pool that knows what a browser is made of — which engine, which arguments,
/// whether there is a server to connect to or a process to start. The pool that owns it knows only that
/// opening one takes a while and can be abandoned.
/// <para>
/// Separated out because it is the one responsibility in the pool that shares no state with the others: it
/// touches neither the page permits, nor the shutdown, nor the browser field they all read. Everything else
/// in there interlocks on purpose, and this did not.
/// </para>
/// </remarks>
internal sealed class Driver(ScrapingOptions options) : IDisposable
{
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
    /// is a faulted task cached for the life of the pool, and every later rent awaits that same fault — one
    /// transient failure at start-up is a pool that never works again. It is left alone because nothing here
    /// can demonstrate it: <see cref="Playwright.CreateAsync"/> is static, so no test in this suite can make
    /// it fail, and a retry written against a failure nobody can produce is a guess about the hot path rather
    /// than a fix. Making it testable means giving the pool a driver factory to be handed, which is the change
    /// to weigh — not a retry bolted on here.
    /// </para>
    /// <para>
    /// THAT CHANGE IS NOW ONE CONSTRUCTOR PARAMETER AWAY, which is the only thing this extraction bought
    /// towards it: a pool handed a <see cref="Driver"/> rather than making one can be handed a fake that
    /// fails. It is still not made, because the argument above has not changed — what is missing is a
    /// measurement of how a driver actually fails in the field, not the ability to write the retry.
    /// </para>
    /// </remarks>
    private readonly Lazy<Task<IPlaywright>> _started = new(Playwright.CreateAsync);

    /// <summary>Whether a browser server is named, so browsers are connected to rather than launched.</summary>
    /// <remarks>
    /// Kept here rather than beside the settings because every consequence of it is a consequence for opening:
    /// a connection carries no command line, a connection cannot be reaped, and a connection says "connected"
    /// in the log where a process says "launched".
    /// </remarks>
    internal bool IsRemote => !string.IsNullOrWhiteSpace(options.BrowserServer);

    /// <summary>A browser of the configured engine, connected to a server or launched as a process.</summary>
    /// <remarks>
    /// The token is honoured only up to the point Playwright accepts one. <c>ConnectAsync</c> and
    /// <c>LaunchAsync</c> take a timeout and no cancellation token, so once either has begun it cannot be told
    /// to stop — which is why the caller releases itself and closes whatever arrives afterwards, rather than
    /// expecting this to end on demand.
    /// </remarks>
    internal async Task<IBrowser> OpenedAsync(CancellationToken ct)
    {
        var started = await _started.Value.WaitAsync(ct);

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

    private IBrowserType Chosen(IPlaywright driver) => options.Engine switch
    {
        Engine.Firefox => driver.Firefox,
        Engine.WebKit => driver.Webkit,
        _ => driver.Chromium
    };

    /// <summary>Releases the driver, and only if it ever started successfully.</summary>
    /// <remarks>
    /// A driver that was never asked for has nothing to release, and one whose start faulted has nothing
    /// either — reading <see cref="Task{TResult}.Result"/> off it would raise that old fault out of a
    /// shutdown, which is the last place anybody wants to meet it.
    /// </remarks>
    public void Dispose()
    {
        if (_started.IsValueCreated && _started.Value.IsCompletedSuccessfully)
            _started.Value.Result.Dispose();
    }
}
