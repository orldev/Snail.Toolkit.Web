namespace Snail.Toolkit.Web.Identity;

/// <summary>
/// Everything a site can observe about who is visiting, decided in one place.
/// </summary>
/// <remarks>
/// This type exists to end a contradiction that defeated a whole stealth layer. The request headers and the
/// injected script used to be produced independently: the script hard-coded <c>navigator.platform = 'Win32'</c>
/// and Russian <c>navigator.languages</c> while the profile generator picked a macOS user agent and a German
/// locale. The resulting fingerprint belongs to no real browser and is more identifying than sending nothing
/// at all. Every consumer now reads the same identity.
/// Everything a browser alone can show — viewport, device scale, touch — is deliberately absent: this type is
/// shared with an HTTP transport that has no such thing to report, and a field nobody can honour is a lie
/// waiting to be told.
/// </remarks>
public sealed record WebIdentity
{
    /// <summary>Gets the user agent string sent and reported to scripts.</summary>
    public required string UserAgent { get; init; }

    /// <summary>Gets the platform as client hints spell it, such as <c>Windows</c> or <c>macOS</c>.</summary>
    public required string Platform { get; init; }

    /// <summary>Gets the platform as <c>navigator.platform</c> spells it, such as <c>Win32</c>.</summary>
    public required string NavigatorPlatform { get; init; }

    /// <summary>Gets the browser brand for client hints, such as <c>Google Chrome</c>.</summary>
    public required string Brand { get; init; }

    /// <summary>Gets the major version the user agent claims.</summary>
    public required string MajorVersion { get; init; }

    /// <summary>Gets the BCP 47 locale, such as <c>ru-RU</c>.</summary>
    public required string Locale { get; init; }

    /// <summary>Gets the IANA time zone, such as <c>Europe/Moscow</c>.</summary>
    public required string Timezone { get; init; }

    /// <summary>Gets the <c>Accept-Language</c> header value.</summary>
    public required string AcceptLanguage { get; init; }

    /// <summary>Gets the values <c>navigator.languages</c> reports, derived from <see cref="AcceptLanguage"/>.</summary>
    public required IReadOnlyList<string> Languages { get; init; }

    /// <summary>Gets the value <c>navigator.hardwareConcurrency</c> reports.</summary>
    public int HardwareConcurrency { get; init; } = 8;

    /// <summary>Gets the value <c>navigator.deviceMemory</c> reports, in gigabytes.</summary>
    public int DeviceMemory { get; init; } = 8;

    /// <summary>Gets the proxy this identity is routed through, when there is one.</summary>
    public ProxyEndpoint? Proxy { get; init; }

    /// <summary>Gets whether client hints apply, which they do for Chromium brands only.</summary>
    public bool SupportsClientHints => Brand is "Google Chrome" or "Microsoft Edge";
}
