using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Identity;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Turns an identity into the browser context that presents it.
/// </summary>
/// <remarks>
/// Only headers a real browser sends on every request are set here. An earlier version put
/// <c>Sec-Fetch-Dest: document</c>, <c>Sec-Fetch-Mode: navigate</c> and <c>Sec-Fetch-Site: none</c> into the
/// context headers, which apply to images, fonts and XHR as well — a combination no browser can produce for a
/// subresource, announced on every request the page made. <c>Accept</c> and <c>Accept-Encoding</c> are left to
/// the browser for the same reason: it varies them per resource type and a fixed value is a mismatch the site
/// can measure.
///
/// Client hints are gated on the engine as well as on the brand. The brand comes from the configured user
/// agent, so a Firefox binary carrying a Chrome user agent would otherwise announce <c>Sec-Ch-Ua</c> on every
/// request — headers Firefox never sends, from a browser claiming to be Chrome. That contradiction is read in
/// one line on the server.
/// </remarks>
internal static class BrowserContexts
{
    /// <param name="identity">The browser this context presents itself as.</param>
    /// <param name="traits">The screen and pointer that identity implies.</param>
    /// <param name="engine">The browser being driven, which decides whether client hints are sent at all.</param>
    /// <param name="ignoresCertificateErrors">Whether a bad certificate stops the visit.</param>
    /// <param name="state">
    /// What the host left on an earlier visit — its cookies and local storage — or nothing the first time.
    /// A cleared challenge lives in exactly those, so giving them back is what makes the second visit cheaper
    /// than the first.
    /// </param>
    public static BrowserNewContextOptions For(
        WebIdentity identity,
        BrowserTraits traits,
        Engine engine,
        bool ignoresCertificateErrors,
        string? state = null)
        => new()
        {
            StorageState = state,
            UserAgent = identity.UserAgent,
            Locale = identity.Locale,
            TimezoneId = identity.Timezone,
            ViewportSize = new ViewportSize { Width = traits.ViewportWidth, Height = traits.ViewportHeight },
            DeviceScaleFactor = traits.DeviceScaleFactor,
            HasTouch = traits.HasTouch,
            IsMobile = false,
            ColorScheme = ColorScheme.Light,
            ReducedMotion = ReducedMotion.NoPreference,
            JavaScriptEnabled = true,
            IgnoreHTTPSErrors = ignoresCertificateErrors,
            ExtraHTTPHeaders = Headers(identity, traits, engine),
            Proxy = identity.Proxy is null
                ? null
                : new Proxy
                {
                    Server = identity.Proxy.Server,
                    Username = identity.Proxy.Username,
                    Password = identity.Proxy.Password
                }
        };

    private static Dictionary<string, string> Headers(WebIdentity identity, BrowserTraits traits, Engine engine)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept-Language"] = identity.AcceptLanguage
        };

        if (engine is not Engine.Chromium || !identity.SupportsClientHints)
            return headers;

        headers["Sec-Ch-Ua"] =
            $"\"Not-A.Brand\";v=\"99\", \"{identity.Brand}\";v=\"{identity.MajorVersion}\", \"Chromium\";v=\"{identity.MajorVersion}\"";
        headers["Sec-Ch-Ua-Mobile"] = traits.HasTouch ? "?1" : "?0";
        headers["Sec-Ch-Ua-Platform"] = $"\"{identity.Platform}\"";

        return headers;
    }
}
