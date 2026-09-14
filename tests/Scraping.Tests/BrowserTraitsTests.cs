using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Identity;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers the traits a browser reports that the shared identity deliberately no longer carries.
/// </summary>
public class BrowserTraitsTests
{
    [Fact]
    public void For_MacIdentity_ReportsARetinaScaleFactor()
        => Assert.Equal(2f, BrowserTraits.For(Identity("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Chrome/124.0.0.0")).DeviceScaleFactor);

    [Fact]
    public void For_WindowsIdentity_ReportsNoScaling()
        => Assert.Equal(1f, BrowserTraits.For(Identity("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0.0.0")).DeviceScaleFactor);

    [Fact]
    public void For_MobileIdentity_ReportsATouchDevice()
        => Assert.True(BrowserTraits.For(Identity("Mozilla/5.0 (Linux; Android 14) Chrome/124.0.0.0 Mobile Safari/537.36")).HasTouch);

    [Fact]
    public void For_DesktopIdentity_ReportsNoTouch()
        => Assert.False(BrowserTraits.For(Identity("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0.0.0")).HasTouch);

    /// <summary>A viewport no real screen has is as identifying as no viewport at all.</summary>
    [Fact]
    public void For_AnyIdentity_DrawsAPlausibleViewport()
    {
        var traits = BrowserTraits.For(Identity("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0.0.0"));

        Assert.InRange(traits.ViewportWidth, 1024, 3840);
        Assert.InRange(traits.ViewportHeight, 600, 2160);
    }

    /// <summary>One identity, one screen — however many visits it is presented on.</summary>
    /// <remarks>
    /// The viewport used to be drawn per visit, so the same browser on the same machine changed screen size
    /// between two page loads. Twenty draws rather than two, because there are four viewports and two agree by
    /// chance one time in four: that is a test the defect passes, and twenty is one it cannot.
    /// <para>
    /// Two different user agents are not asked to differ. What is being fixed is one identity disagreeing with
    /// itself, and a check that also demanded variety would be asserting something nobody measured.
    /// </para>
    /// </remarks>
    [Fact]
    public void For_OneIdentityAskedTwenty_ReportsTheSameScreenEveryTime()
    {
        var screens = Enumerable.Range(0, 20)
            .Select(_ => BrowserTraits.For(Identity(Chrome)))
            .Select(traits => (traits.ViewportWidth, traits.ViewportHeight))
            .Distinct();

        Assert.Single(screens);
    }

    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0.0.0";

    private static WebIdentity Identity(string userAgent) => new()
    {
        UserAgent = userAgent,
        Platform = userAgent.Contains("Macintosh", StringComparison.Ordinal) ? "macOS" : "Windows",
        NavigatorPlatform = "Win32",
        Brand = "Google Chrome",
        MajorVersion = "124",
        Locale = "en-US",
        Timezone = "UTC",
        AcceptLanguage = "en-US,en;q=0.9",
        Languages = ["en-US", "en"]
    };
}
