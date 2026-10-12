using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Identity;

/// <summary>
/// Builds one coherent identity per visit from the configured fingerprints.
/// </summary>
/// <remarks>
/// Every observable trait is derived from a single user agent, so the platform a script reports, the platform
/// client hints announce and the operating system the user agent names cannot disagree. The values used to
/// come from two independent generators and routinely contradicted each other.
/// <para>
/// The fingerprints are handed out in turn, the way <see cref="ProxyRotation"/> hands out addresses and for the
/// same reason: a random pick repeats itself in bursts, and with three configured a run of three picks the same
/// one about one time in nine. A burst of identical fingerprints from one address is the pattern rotation
/// exists to avoid, so drawing at random undid the feature while looking like it.
/// </para>
/// </remarks>
public sealed partial class IdentityRotation : IIdentityRotation
{
    private static readonly Fingerprint[] BuiltIn =
    [
        new()
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            Locale = "ru-RU",
            Timezone = "Europe/Moscow",
            AcceptLanguage = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7"
        },
        new()
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0",
            Locale = "en-US",
            Timezone = "America/New_York",
            AcceptLanguage = "en-US,en;q=0.9"
        },
        new()
        {
            UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            Locale = "en-GB",
            Timezone = "Europe/London",
            AcceptLanguage = "en-GB,en;q=0.9"
        }
    ];

    private readonly Fingerprint[] _fingerprints;
    private readonly IProxyRotation _proxies;
    private int _cursor = -1;

    /// <summary>Reads the configured fingerprints, falling back to a built-in set.</summary>
    public IdentityRotation(IOptions<IdentityOptions> options, IProxyRotation proxies, ILogger<IdentityRotation> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _proxies = proxies;

        var configured = options.Value.Fingerprints
            .Where(fingerprint => !string.IsNullOrWhiteSpace(fingerprint.UserAgent))
            .ToArray();

        _fingerprints = configured.Length > 0 ? configured : BuiltIn;

        if (configured.Length == 0)
            Log.BuiltInFingerprints(logger, BuiltIn.Length, IdentityOptions.SectionName, nameof(IdentityOptions.Fingerprints));

        var onUtc = _fingerprints.Count(fingerprint =>
            string.Equals(fingerprint.Timezone, "UTC", StringComparison.OrdinalIgnoreCase));

        if (onUtc > 0)
            Log.UtcFingerprints(logger, onUtc, IdentityOptions.SectionName, nameof(IdentityOptions.Fingerprints));
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public WebIdentity For(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var steady = Steadily(host);

        return Built(
            _fingerprints[steady % (uint)_fingerprints.Length],
            _proxies.For(host));
    }

    /// <inheritdoc />
    public WebIdentity Next()
    {
        var next = (uint)Interlocked.Increment(ref _cursor);

        return Built(_fingerprints[next % (uint)_fingerprints.Length], _proxies.Next());
    }

    /// <summary>A number the same host answers with in any process, on any machine, forever.</summary>
    /// <remarks>
    /// <see cref="string.GetHashCode()"/> is randomised per process, so a host would be given one browser today
    /// and another after a restart — which is the thing this exists to prevent. FNV-1a over the lower-cased
    /// name is four lines and answers the same everywhere.
    /// </remarks>
    private static uint Steadily(string host)
    {
        var hash = 2166136261u;

        foreach (var character in host)
            hash = (hash ^ char.ToLowerInvariant(character)) * 16777619u;

        return hash;
    }

    private WebIdentity Built(Fingerprint fingerprint, ProxyEndpoint? exit)
    {
        var userAgent = fingerprint.UserAgent;

        return new WebIdentity
        {
            UserAgent = userAgent,
            Platform = PlatformOf(userAgent),
            NavigatorPlatform = NavigatorPlatformOf(userAgent),
            Brand = BrandOf(userAgent),
            MajorVersion = MajorVersionOf(userAgent),
            Locale = fingerprint.Locale,
            Timezone = fingerprint.Timezone,
            AcceptLanguage = fingerprint.AcceptLanguage,
            Languages = LanguagesOf(fingerprint.AcceptLanguage),
            Proxy = exit
        };
    }

    private static string BrandOf(string userAgent) => userAgent switch
    {
        _ when userAgent.Contains("Edg/", StringComparison.Ordinal) => "Microsoft Edge",
        _ when userAgent.Contains("Firefox/", StringComparison.Ordinal) => "Firefox",
        _ when userAgent.Contains("Chrome/", StringComparison.Ordinal) => "Google Chrome",
        _ => "Safari"
    };

    private static string PlatformOf(string userAgent) => userAgent switch
    {
        _ when userAgent.Contains("Windows", StringComparison.Ordinal) => "Windows",
        _ when userAgent.Contains("Android", StringComparison.Ordinal) => "Android",
        _ when userAgent.Contains("Macintosh", StringComparison.Ordinal) => "macOS",
        _ when userAgent.Contains("Linux", StringComparison.Ordinal) => "Linux",
        _ => "iOS"
    };

    private static string NavigatorPlatformOf(string userAgent) => PlatformOf(userAgent) switch
    {
        "Windows" => "Win32",
        "macOS" => "MacIntel",
        "Android" => "Linux armv8l",
        "Linux" => "Linux x86_64",
        _ => "iPhone"
    };

    private static string MajorVersionOf(string userAgent)
    {
        var match = VersionPattern().Match(userAgent);

        return match.Success ? match.Groups[1].Value : "124";
    }

    private static string[] LanguagesOf(string acceptLanguage)
        => [.. acceptLanguage
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Split(';', 2)[0].Trim())
            .Where(tag => tag.Length > 0)];

    [GeneratedRegex(@"(?:Chrome|Edg|Firefox|Version)/(\d+)")]
    private static partial Regex VersionPattern();
}
