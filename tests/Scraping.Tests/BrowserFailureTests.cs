using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers the classification every caller that drives a browser shares.
/// </summary>
/// <remarks>
/// The page reader classified and the agent and the site call did not: everything a browser threw at them
/// became <see cref="FailureKind.Unexpected"/>, which is not transient, so a browser that had lost its
/// connection was indistinguishable from a selector that names nothing.
/// </remarks>
public class BrowserFailureTests
{
    [Theory]
    [InlineData("Timeout 30000ms exceeded.", FailureKind.Timeout)]
    [InlineData("Target closed", FailureKind.BrowserUnavailable)]
    [InlineData("Browser closed", FailureKind.BrowserUnavailable)]
    [InlineData("Target page, context or browser has been closed", FailureKind.BrowserUnavailable)]
    [InlineData("net::ERR_PROXY_CONNECTION_FAILED at https://example.com", FailureKind.Proxy)]
    [InlineData("net::ERR_TUNNEL_CONNECTION_FAILED at https://example.com", FailureKind.Proxy)]
    [InlineData("net::ERR_NAME_NOT_RESOLVED at https://example.com", FailureKind.Navigation)]
    public void Of_PlaywrightMessage_IsReadAsTheKindItNames(string message, FailureKind expected)
        => Assert.Equal(expected, Failure(new PlaywrightException(message)).Kind);

    /// <summary>A locator wait is the one place Playwright 1.62 raises the framework's own timeout.</summary>
    [Fact]
    public void Of_TimeoutFromALocatorWait_IsATimeout()
        => Assert.Equal(FailureKind.Timeout, Failure(new TimeoutException("Locator.WaitForAsync timed out")).Kind);

    /// <summary>A browser that went away is worth another identity; that is the whole point of classifying.</summary>
    [Theory]
    [InlineData("Target closed")]
    [InlineData("Timeout 30000ms exceeded.")]
    [InlineData("net::ERR_PROXY_CONNECTION_FAILED")]
    public void Of_BrowserThatWentAway_IsTransient(string message)
        => Assert.True(Failure(new PlaywrightException(message)).IsTransient);

    [Fact]
    public void Of_AnyBrowserFailure_CarriesTheAddressTheIdentityAndTheCause()
    {
        var cause = new PlaywrightException("Target closed");

        var failure = Failure(cause);

        Assert.Equal(Link.Of("https://example.com/a"), failure.Address);
        Assert.Equal("Agent/1.0", failure.UserAgent);
        Assert.Same(cause, failure.Cause);
    }

    [Theory]
    [InlineData(typeof(PlaywrightException))]
    [InlineData(typeof(TimeoutException))]
    public void IsBrowsers_ExceptionABrowserRaises_IsRecognised(Type kind)
        => Assert.True(BrowserFailure.IsBrowsers((Exception)Activator.CreateInstance(kind, "message")!));

    /// <summary>Anything else is a defect in this library rather than an answer from the site, and must escape.</summary>
    [Fact]
    public void IsBrowsers_ExceptionNoBrowserRaises_IsNotRecognised()
        => Assert.False(BrowserFailure.IsBrowsers(new InvalidOperationException("a defect")));

    private static PageFailure Failure(Exception exception)
        => BrowserFailure.Of(exception, Link.Of("https://example.com/a"), new WebIdentity
        {
            UserAgent = "Agent/1.0",
            Platform = "Windows",
            NavigatorPlatform = "Win32",
            Brand = "Google Chrome",
            MajorVersion = "124",
            Locale = "en-US",
            Timezone = "UTC",
            AcceptLanguage = "en-US,en;q=0.9",
            Languages = ["en-US", "en"]
        });
}
