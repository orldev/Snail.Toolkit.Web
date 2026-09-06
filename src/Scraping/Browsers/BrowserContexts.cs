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
/// </remarks>
internal static class BrowserContexts
{
    public static BrowserNewContextOptions For(
        WebIdentity identity,
        BrowserTraits traits,
        bool ignoresCertificateErrors)
        => new()
        {
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
            ExtraHTTPHeaders = Headers(identity, traits),
            Proxy = identity.Proxy is null
                ? null
                : new Proxy
                {
                    Server = identity.Proxy.Server,
                    Username = identity.Proxy.Username,
                    Password = identity.Proxy.Password
                }
        };

    private static Dictionary<string, string> Headers(WebIdentity identity, BrowserTraits traits)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept-Language"] = identity.AcceptLanguage
        };

        if (!identity.SupportsClientHints)
            return headers;

        headers["Sec-Ch-Ua"] =
            $"\"Not-A.Brand\";v=\"99\", \"{identity.Brand}\";v=\"{identity.MajorVersion}\", \"Chromium\";v=\"{identity.MajorVersion}\"";
        headers["Sec-Ch-Ua-Mobile"] = traits.HasTouch ? "?1" : "?0";
        headers["Sec-Ch-Ua-Platform"] = $"\"{identity.Platform}\"";

        return headers;
    }
}
