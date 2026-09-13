using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Browsers;
using Snail.Toolkit.Web.Scraping.Identity;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers the rule that outlives any single header: nothing the browser announces may contradict the engine
/// that announces it.
/// </summary>
public class BrowserContextsTests
{
    /// <summary>
    /// The brand comes from the configured user agent, so a Firefox binary carrying a Chrome user agent would
    /// otherwise announce Sec-Ch-Ua on every request — headers Firefox never sends, from a browser claiming to
    /// be Chrome. The contradiction is read in one line on the server.
    /// </summary>
    [Fact]
    public void For_FirefoxEngine_SendsNoClientHints()
    {
        var options = BrowserContexts.For(Chrome, Traits, Engine.Firefox, false);

        Assert.DoesNotContain(options.ExtraHTTPHeaders!, header => header.Key.StartsWith("Sec-Ch-Ua", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void For_WebKitEngine_SendsNoClientHints()
    {
        var options = BrowserContexts.For(Chrome, Traits, Engine.WebKit, false);

        Assert.DoesNotContain(options.ExtraHTTPHeaders!, header => header.Key.StartsWith("Sec-Ch-Ua", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void For_ChromiumEngine_SendsClientHints()
    {
        var options = BrowserContexts.For(Chrome, Traits, Engine.Chromium, false);

        Assert.Contains(options.ExtraHTTPHeaders!, header => header.Key.Equals("Sec-Ch-Ua", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void For_AnyEngine_StillSendsAcceptLanguage()
    {
        var options = BrowserContexts.For(Chrome, Traits, Engine.Firefox, false);

        Assert.Contains(options.ExtraHTTPHeaders!, header => header.Key.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Engine_LeftUnset_IsChromium()
        => Assert.Equal(Engine.Chromium, new ScrapingOptions().Engine);

    private static WebIdentity Chrome => new()
    {
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        Platform = "Windows",
        NavigatorPlatform = "Win32",
        Brand = "Google Chrome",
        MajorVersion = "124",
        Locale = "en-US",
        Timezone = "America/New_York",
        AcceptLanguage = "en-US,en;q=0.9",
        Languages = ["en-US", "en"]
    };

    private static BrowserTraits Traits => BrowserTraits.For(Chrome);
}
