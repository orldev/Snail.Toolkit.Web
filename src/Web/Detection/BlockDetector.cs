using System.Text.RegularExpressions;

namespace Snail.Toolkit.Web.Detection;

/// <summary>
/// Matches the markers challenge and consent pages leave in the document or in the final URL.
/// </summary>
/// <remarks>
/// The URL is inspected as well as the body because the most common refusals are redirects: Google answers
/// with <c>/sorry/</c> and sends European visitors to a consent host, and neither page carries any of the
/// markup markers. An earlier version watched the body alone, so a refusal arrived as a successful fetch whose
/// parse found nothing, and the failure was recorded as "empty" rather than "blocked".
/// The generic words <c>captcha</c> and <c>blocked</c> are deliberately absent from both lists: an article
/// that discusses captchas would match them, and a false block is worse than a missed one — it burns a proxy
/// and a retry.
/// <para>
/// That rule was written for the markup and broken by the URL beside it, where the bare word stood until
/// 2026-09-13: any address carrying it — <c>/blog/how-captcha-works</c> — was called a refusal, and because
/// a refusal is transient the reader then spent three identities proving it again. A signal belongs here only
/// when the words are a refusal wherever they appear, which is why what is left are whole paths and hosts.
/// </para>
/// </remarks>
public sealed partial class BlockDetector : IBlockDetector
{
    private static readonly string[] MarkupSignals =
    [
        "cf-browser-verification",
        "cf-challenge",
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
        "/cdn-cgi/challenge",
        "challenges.cloudflare.com"
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

        return Challenge().IsMatch(html) ? BlockVerdict.Blocked("markup:g-recaptcha") : BlockVerdict.Clear;
    }

    /// <summary>Matches a reCAPTCHA challenge widget without matching the invisible token field.</summary>
    /// <remarks>
    /// Measured on a live registry on 2026-09-12: its rendered page carries <c>g-recaptcha</c> three times and
    /// every one of them belongs to the hidden <c>g-recaptcha-response</c> field that reCAPTCHA v3 injects into
    /// any form it guards. A plain substring match therefore called a page that had answered perfectly well a
    /// challenge — the false block the rest of this class is careful to avoid — and it did so on precisely the
    /// pages worth asking, since a form guarded by an invisible token is one whose service answers no one but
    /// its own page. The server's HTML carries none of this, so only a rendered document reproduces it.
    /// <para>
    /// The visible widget still matches: reCAPTCHA v2 marks it <c>class="g-recaptcha"</c>, with nothing after
    /// the word. Whether other invisible challenges inject a field of the same shape has not been measured
    /// here, so their markers are left as they were.
    /// </para>
    /// </remarks>
    [GeneratedRegex("g-recaptcha(?!-response)", RegexOptions.IgnoreCase)]
    private static partial Regex Challenge();
}
