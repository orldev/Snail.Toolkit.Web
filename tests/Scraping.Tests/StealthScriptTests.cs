using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Stealth;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// The stealth script aligns what a browser already exposes and declares nothing it does not.
/// </summary>
public sealed class StealthScriptTests
{
    /// <summary>
    /// A build that reports <c>navigator.webdriver</c> as <c>false</c> in C++ has to keep its native getter.
    /// </summary>
    /// <remarks>
    /// Measured on 13 September 2026 against the patched Firefox: the same recipe returned results through
    /// bare Playwright and was answered with <c>/sorry/</c> through the toolkit, twice around a passing
    /// control. Redefining the property replaced a native getter with one whose <c>toString</c> announces the
    /// spoof — the state the patched build exists to avoid.
    /// </remarks>
    [Fact]
    public void For_Always_RedefinesWebdriverOnlyWhereTheBrowserAnnouncesIt()
    {
        var script = new StealthScript().For(Identity);

        Assert.Contains(
            "if (navigator.webdriver === true) define('webdriver', false)",
            script,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Chromium-only traits are not invented on an engine that has none of them.
    /// </summary>
    /// <remarks>
    /// <c>deviceMemory</c>, <c>navigator.connection</c> and <c>window.chrome</c> do not exist in Gecko, so
    /// declaring them turns a Firefox browser into one answering Chrome questions — a contradiction a site
    /// reads in one line, and a worse signal than the plain absence they replace.
    /// </remarks>
    [Fact]
    public void For_Always_DeclaresChromiumTraitsOnlyWhereTheyAlreadyExist()
    {
        var script = new StealthScript().For(Identity);

        Assert.Contains("if ('deviceMemory' in navigator)", script, StringComparison.Ordinal);
        Assert.Contains("if (!('connection' in navigator)) return;", script, StringComparison.Ordinal);
        Assert.Contains("if (navigator.vendor !== 'Google Inc.') return;", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// A browser that lists plugins of its own keeps them.
    /// </summary>
    /// <remarks>
    /// The substitute list exists for a browser reporting none at all. The patched build reports five, and
    /// replacing a real list with a shorter invented one is a difference rather than a disguise.
    /// </remarks>
    [Fact]
    public void For_Always_SubstitutesPluginsOnlyWhereThereAreNone()
    {
        var script = new StealthScript().For(Identity);

        Assert.Contains("if (navigator.plugins.length > 0) return;", script, StringComparison.Ordinal);
    }

    private static WebIdentity Identity => new()
    {
        UserAgent = "Mozilla/5.0 (X11; Linux x86_64; rv:155.0) Gecko/20100101 Firefox/155.0",
        Platform = "Linux",
        NavigatorPlatform = "Linux x86_64",
        Brand = "Firefox",
        MajorVersion = "155",
        Locale = "en-US",
        Timezone = "America/New_York",
        AcceptLanguage = "en-US,en;q=0.9",
        Languages = ["en-US", "en"]
    };
}
