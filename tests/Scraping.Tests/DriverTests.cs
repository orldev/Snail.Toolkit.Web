using Microsoft.Playwright;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers the one thing the driver holds: the Playwright installation, started once and kept.
/// </summary>
/// <remarks>
/// Kept, but not kept when it failed. The driver used to hold a <c>Lazy&lt;Task&lt;IPlaywright&gt;&gt;</c>, which
/// caches the task itself — including a faulted one. A single failure to start, of the kind a machine produces
/// while it is busy or while the driver is being unpacked, therefore ended every later rent of a browser with
/// the same exception until the process was restarted. It was named in the code as undemonstrable on
/// 2026-09-15, because <c>Playwright.CreateAsync</c> is static and no test can make it fail; the answer was to
/// stop asking for it statically.
/// </remarks>
public class DriverTests
{
    [Fact]
    public async Task OpenedAsync_StartThatFailed_IsAttemptedAgainRatherThanRemembered()
    {
        var attempts = 0;
        using var driver = new Driver(new ScrapingOptions(), () =>
        {
            attempts++;

            return Task.FromException<IPlaywright>(new InvalidOperationException("driver not unpacked yet"));
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.OpenedAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.OpenedAsync(CancellationToken.None));

        Assert.Equal(2, attempts);
    }

    /// <summary>
    /// A start that worked is still asked for once: the installation is the expensive part, and the pair of
    /// these two is what says the cure did not simply switch the caching off.
    /// </summary>
    [Fact]
    public async Task OpenedAsync_StartThatWorked_IsAskedForOnce()
    {
        var attempts = 0;
        using var driver = new Driver(new ScrapingOptions { BrowserServer = "ws://localhost:1/" }, () =>
        {
            attempts++;

            return Task.FromResult<IPlaywright>(new Absent());
        });

        await Assert.ThrowsAsync<NotSupportedException>(() => driver.OpenedAsync(CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() => driver.OpenedAsync(CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    /// <summary>A Playwright that answers nothing: the test needs it to exist, never to drive a browser.</summary>
    private sealed class Absent : IPlaywright
    {
        public IBrowserType Chromium => throw new NotSupportedException();

        public IBrowserType Firefox => throw new NotSupportedException();

        public IBrowserType Webkit => throw new NotSupportedException();

        public ISelectors Selectors => throw new NotSupportedException();

        public IReadOnlyDictionary<string, BrowserNewContextOptions> Devices => throw new NotSupportedException();

        public IAPIRequest APIRequest => throw new NotSupportedException();

        public IBrowserType this[string browserType] => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
