namespace Snail.Toolkit.Web.Detection;

/// <summary>
/// Matches the markers challenge and consent pages leave in the document or in the final URL.
/// </summary>
/// <remarks>
/// The URL is inspected as well as the body because the most common refusals are redirects: Google answers
/// with <c>/sorry/</c> and sends European visitors to a consent host, and neither page carries any of the
/// markup markers. An earlier version watched the body alone, so a refusal arrived as a successful fetch whose
/// parse found nothing, and the failure was recorded as "empty" rather than "blocked".
/// The generic words <c>captcha</c> and <c>blocked</c> are deliberately absent from the markup list: an
/// article that discusses captchas would match them, and a false block is worse than a missed one — it burns
/// a proxy and a retry.
/// </remarks>
public sealed class BlockDetector : IBlockDetector
{
    private static readonly string[] MarkupSignals =
    [
        "cf-browser-verification",
        "cf-challenge",
        "g-recaptcha",
        "h-captcha",
        "hcaptcha",
        "checking your browser",
        "ddos protection",
        "access denied",
        "unusual traffic",
        "are you a robot",
        "enable javascript and cookies to continue",
        "request blocked",
        "attention required! | cloudflare"
    ];

    private static readonly string[] UrlSignals =
    [
        "/sorry/",
        "consent.google.com",
        "/challenge-platform/",
        "captcha"
    ];

    /// <inheritdoc />
    public BlockVerdict Inspect(string html, string finalUrl)
    {
        foreach (var signal in UrlSignals)
        {
            if (finalUrl.Contains(signal, StringComparison.OrdinalIgnoreCase))
                return BlockVerdict.Blocked($"url:{signal}");
        }

        if (string.IsNullOrEmpty(html))
            return BlockVerdict.Clear;

        foreach (var signal in MarkupSignals)
        {
            if (html.Contains(signal, StringComparison.OrdinalIgnoreCase))
                return BlockVerdict.Blocked($"markup:{signal}");
        }

        return BlockVerdict.Clear;
    }
}
