namespace Snail.Toolkit.Web.Identity;

/// <summary>
/// One fingerprint the toolkit may present, as configuration supplies it.
/// </summary>
/// <remarks>
/// Fingerprints are configuration rather than a compiled array. An earlier version froze four Chrome 123 and
/// 124 user agents into a <c>private static readonly</c> field; those versions shipped in April 2024, and a
/// browser claiming them today is more suspicious than one claiming nothing. Refreshing the list must not
/// require a release of this library.
/// </remarks>
public sealed class Fingerprint
{
    /// <summary>Gets or sets the user agent string. Platform, brand and version are derived from it.</summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>Gets or sets the BCP 47 locale, such as <c>ru-RU</c>.</summary>
    public string Locale { get; set; } = "en-US";

    /// <summary>Gets or sets the IANA time zone, such as <c>Europe/Moscow</c>.</summary>
    public string Timezone { get; set; } = "UTC";

    /// <summary>Gets or sets the <c>Accept-Language</c> header value.</summary>
    public string AcceptLanguage { get; set; } = "en-US,en;q=0.9";
}
