using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Browsers;
using Snail.Toolkit.Web.Scraping.Identity;

namespace Snail.Toolkit.Web.Scraping.Stealth;

/// <summary>
/// Rewrites the browser's user-agent metadata over CDP so a page reads the identity it was told about.
/// </summary>
/// <remarks>
/// Measured on 2026-09-11 against a live site: the context sent <c>Sec-Ch-Ua: "Google Chrome";v="140"</c> while
/// the page still read <c>HeadlessChrome;151</c> from <c>navigator.userAgentData</c>, and the site's analytics
/// carried the headless build away in its <c>uafvl</c> parameter. Playwright has no context option for this —
/// <c>BrowserNewContextOptions.UserAgent</c> spoofs the user-agent string alone — so the metadata has to be
/// set through <c>Emulation.setUserAgentOverride</c>, which only Chromium answers. A header and a script that
/// disagree are a stronger signal than either would be on its own, which is why failing to align is reported
/// rather than passed over.
///
/// Chromium only, and the check is on the engine rather than on the exception it would raise:
/// <c>NewCDPSessionAsync</c> throws on Firefox and WebKit, and a warning logged on every page of a Firefox run
/// would say nothing anyone can act on.
/// </remarks>
public sealed class ClientHints(IOptions<ScrapingOptions> options, ILogger<ClientHints> logger) : IClientHints
{
    private const string Placeholder = "Not-A.Brand";

    /// <inheritdoc />
    public async Task AlignAsync(
        IPage page,
        WebIdentity identity,
        BrowserTraits traits,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(traits);

        if (options.Value.Engine is not Engine.Chromium || !identity.SupportsClientHints)
            return;

        ct.ThrowIfCancellationRequested();

        try
        {
            var session = await page.Context.NewCDPSessionAsync(page);

            await session.SendAsync("Emulation.setUserAgentOverride", Claim(identity, traits));
        }
        catch (PlaywrightException exception)
        {
            logger.LogWarning(
                exception,
                "Client hints stay as the browser built them while the headers claim {Brand} {Version}; the two "
                + "disagree and that disagreement is itself detectable.",
                identity.Brand, identity.MajorVersion);
        }
    }

    private static Dictionary<string, object> Claim(WebIdentity identity, BrowserTraits traits)
        => new()
        {
            ["userAgent"] = identity.UserAgent,
            ["acceptLanguage"] = identity.AcceptLanguage,
            ["platform"] = identity.NavigatorPlatform,
            ["userAgentMetadata"] = new Dictionary<string, object>
            {
                ["brands"] = Brands(identity),
                ["fullVersion"] = $"{identity.MajorVersion}.0.0.0",
                ["platform"] = identity.Platform,
                ["platformVersion"] = VersionOf(identity.Platform),
                ["architecture"] = "x86",
                ["model"] = string.Empty,
                ["mobile"] = traits.HasTouch
            }
        };

    private static List<Dictionary<string, string>> Brands(WebIdentity identity) =>
    [
        new() { ["brand"] = Placeholder, ["version"] = "99" },
        new() { ["brand"] = identity.Brand, ["version"] = identity.MajorVersion },
        new() { ["brand"] = "Chromium", ["version"] = identity.MajorVersion }
    ];

    private static string VersionOf(string platform)
        => platform switch
        {
            "macOS" => "10.15.7",
            "Windows" => "10.0.0",
            _ => string.Empty
        };
}
