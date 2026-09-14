using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the rule that made this type exist: nothing a site can measure may contradict anything else.
/// </summary>
public class IdentityRotationTests
{
    /// <summary>
    /// The traits used to come from two independent generators, which produced a macOS user agent alongside a
    /// Win32 platform — a fingerprint belonging to no real browser and more identifying than sending nothing.
    /// </summary>
    [Fact]
    public void Next_MacUserAgent_DerivesEveryTraitFromIt()
    {
        var identity = Rotation(new Fingerprint
        {
            UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            Locale = "en-GB",
            Timezone = "Europe/London",
            AcceptLanguage = "en-GB,en;q=0.9"
        }).Next();

        Assert.Equal("macOS", identity.Platform);
        Assert.Equal("MacIntel", identity.NavigatorPlatform);
        Assert.Equal("Google Chrome", identity.Brand);
        Assert.Equal("131", identity.MajorVersion);
    }

    [Fact]
    public void Next_EdgeUserAgent_ReportsTheEdgeBrand()
    {
        var identity = Rotation(new Fingerprint
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0"
        }).Next();

        Assert.Equal("Microsoft Edge", identity.Brand);
        Assert.Equal("Win32", identity.NavigatorPlatform);
        Assert.True(identity.SupportsClientHints);
    }

    [Fact]
    public void Next_AcceptLanguage_BecomesTheLanguagesScriptsReport()
    {
        var identity = Rotation(new Fingerprint
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0.0.0",
            AcceptLanguage = "ru-RU,ru;q=0.9,en-US;q=0.8"
        }).Next();

        Assert.Equal(["ru-RU", "ru", "en-US"], identity.Languages);
    }

    [Fact]
    public void Next_WithoutConfiguredFingerprints_StillProducesAnIdentity()
    {
        var identity = Rotation().Next();

        Assert.False(string.IsNullOrWhiteSpace(identity.UserAgent));
        Assert.False(string.IsNullOrWhiteSpace(identity.Platform));
    }

    /// <summary>The fingerprints go round in turn, for the same reason the addresses do.</summary>
    /// <remarks>
    /// Was a random pick, which with three configured repeats the same one three times running about once in
    /// nine — a burst of identical fingerprints from a single address, which is the pattern rotation is here to
    /// avoid. Three calls over two fingerprints say it in one line: a random draw cannot promise the third.
    /// </remarks>
    [Fact]
    public void Next_WithSeveralFingerprints_HandsThemOutInTurn()
    {
        var rotation = Rotation(
            new Fingerprint { UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0.0.0" },
            new Fingerprint { UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Chrome/124.0.0.0" });

        Assert.Equal(["Windows", "macOS", "Windows"], [rotation.Next().Platform, rotation.Next().Platform, rotation.Next().Platform]);
    }

    /// <summary>A round-robin, not a random pick: random reuse comes in bursts, and bursts are what get banned.</summary>
    [Fact]
    public void Next_WithProxiesConfigured_HandsThemOutInTurn()
    {
        var options = new IdentityOptions { Proxies = ["http://one.example:8080", "http://two.example:8080"] };
        var proxies = new ProxyRotation(Options.Create(options));

        Assert.Equal("http://one.example:8080", proxies.Next()!.Server);
        Assert.Equal("http://two.example:8080", proxies.Next()!.Server);
        Assert.Equal("http://one.example:8080", proxies.Next()!.Server);
    }

    [Fact]
    public void Next_WithoutProxiesConfigured_IsNotRoutedAnywhere()
        => Assert.Null(new ProxyRotation(Options.Create(new IdentityOptions())).Next());

    [Fact]
    public void ProxyRotation_MalformedProxy_RefusesToStart()
    {
        var options = Options.Create(new IdentityOptions { Proxies = ["not a proxy"] });

        Assert.Throws<OptionsValidationException>(() => new ProxyRotation(options));
    }

    /// <summary>
    /// Measured on 13 September 2026 from one residential address, a fresh browser profile per attempt:
    /// Google answered /sorry/ on UTC and returned results on both America/New_York and Europe/Moscow. UTC is
    /// what a container defaults to, so it is the one zone a fingerprint must not fall into by omission.
    /// </summary>
    [Fact]
    public void Timezone_LeftUnset_IsNotTheZoneContainersDefaultTo()
        => Assert.NotEqual("UTC", new Fingerprint().Timezone, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Rotation_FingerprintOnUtc_WarnsAboutTheZone()
    {
        var logger = new RecordingLogger<IdentityRotation>();

        _ = Rotation(logger, new Fingerprint { UserAgent = KnownAgent, Timezone = "UTC" });

        Assert.Contains(logger.Warnings, warning => warning.Contains("UTC", StringComparison.Ordinal));
    }

    [Fact]
    public void Rotation_FingerprintOnARealZone_SaysNothingAboutUtc()
    {
        var logger = new RecordingLogger<IdentityRotation>();

        _ = Rotation(logger, new Fingerprint { UserAgent = KnownAgent, Timezone = "Europe/London" });

        Assert.DoesNotContain(logger.Warnings, warning => warning.Contains("UTC", StringComparison.Ordinal));
    }

    private const string KnownAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private static IdentityRotation Rotation(ILogger<IdentityRotation> logger, params Fingerprint[] fingerprints)
        => new(
            Options.Create(new IdentityOptions { Fingerprints = [.. fingerprints] }),
            new ProxyRotation(Options.Create(new IdentityOptions())),
            logger);

    private static IdentityRotation Rotation(params Fingerprint[] fingerprints)
        => new(
            Options.Create(new IdentityOptions { Fingerprints = [.. fingerprints] }),
            new ProxyRotation(Options.Create(new IdentityOptions())),
            NullLogger<IdentityRotation>.Instance);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel is LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
