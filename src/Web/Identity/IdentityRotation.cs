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
    private readonly Shaped?[] _shaped;
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
        _shaped = new Shaped?[_fingerprints.Length];

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

        return Built((int)(steady % (uint)_fingerprints.Length), _proxies.For(host));
    }

    /// <inheritdoc />
    public WebIdentity Next()
    {
        var next = (uint)Interlocked.Increment(ref _cursor);

        return Built((int)(next % (uint)_fingerprints.Length), _proxies.Next());
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

    /// <summary>The identity of the fingerprint at <paramref name="index"/>, leaving by <paramref name="exit"/>.</summary>
    /// <remarks>
    /// Everything an identity says is derived from its fingerprint alone, and it was derived again on every
    /// request — a regular expression, three chains of comparisons and a split of the languages: measured on
    /// 2026-10-12 by the audit, about 300 ns and 1 021 bytes a request, for the same answer each time. The
    /// identity is now shaped once per fingerprint and handed out again, with the exit set when a proxy is in
    /// use. It is shaped anew whenever the fingerprint's values are no longer the ones it was shaped from, since
    /// a fingerprint is a settings object anyone holding it can change.
    /// <para>
    /// Since every identity of one fingerprint now carries the same list of languages, the list is handed out
    /// read-only: as an array, a caller casting it back could have rewritten the languages of every identity
    /// after its own.
    /// </para>
    /// </remarks>
    private WebIdentity Built(int index, ProxyEndpoint? exit)
    {
        var fingerprint = _fingerprints[index];

        if (Volatile.Read(ref _shaped[index]) is not { } shaped || !shaped.IsOf(fingerprint))
        {
            shaped = new Shaped(fingerprint);
            Volatile.Write(ref _shaped[index], shaped);
        }

        return exit is null ? shaped.Identity : shaped.Identity with { Proxy = exit };
    }

    private static WebIdentity Built(string userAgent, string locale, string timezone, string acceptLanguage)
        => new()
        {
            UserAgent = userAgent,
            Platform = PlatformOf(userAgent),
            NavigatorPlatform = NavigatorPlatformOf(userAgent),
            Brand = BrandOf(userAgent),
            MajorVersion = MajorVersionOf(userAgent),
            Locale = locale,
            Timezone = timezone,
            AcceptLanguage = acceptLanguage,
            Languages = Array.AsReadOnly(LanguagesOf(acceptLanguage))
        };

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

    /// <summary>An identity, and the very values of the fingerprint it was shaped from.</summary>
    private sealed class Shaped
    {
        private readonly string _userAgent;
        private readonly string _locale;
        private readonly string _timezone;
        private readonly string _acceptLanguage;

        public Shaped(Fingerprint fingerprint)
        {
            _userAgent = fingerprint.UserAgent;
            _locale = fingerprint.Locale;
            _timezone = fingerprint.Timezone;
            _acceptLanguage = fingerprint.AcceptLanguage;

            Identity = Built(_userAgent, _locale, _timezone, _acceptLanguage);
        }

        public WebIdentity Identity { get; }

        public bool IsOf(Fingerprint current)
            => ReferenceEquals(_userAgent, current.UserAgent)
               && ReferenceEquals(_locale, current.Locale)
               && ReferenceEquals(_timezone, current.Timezone)
               && ReferenceEquals(_acceptLanguage, current.AcceptLanguage);
    }
}
