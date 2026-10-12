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

    /// <summary>
    /// One host always sees the same browser, because a site that let us in remembers whom it let in.
    /// </summary>
    /// <remarks>
    /// A challenge that a site clears hands back a cookie bound to the address AND the user agent that solved
    /// it — Cloudflare's <c>cf_clearance</c> is the common one. Keeping that cookie while drawing the next
    /// identity from the round makes it worthless and the visit stranger than one with no cookie at all: a
    /// returning visitor whose browser changed between requests. So the identity for a host is drawn once and
    /// stays; <see cref="IIdentityRotation.Next"/> keeps handing them out in turn for everything else.
    /// </remarks>
    [Fact]
    public void For_TheSameHost_IsAlwaysTheSameIdentity()
    {
        var rotation = Rotation(
            new Fingerprint { UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0.0.0" },
            new Fingerprint { UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Chrome/124.0.0.0" },
            new Fingerprint { UserAgent = "Mozilla/5.0 (X11; Linux x86_64) Chrome/124.0.0.0" });

        var first = rotation.For("registry.example");

        rotation.Next();
        rotation.Next();

        Assert.Equal(first.UserAgent, rotation.For("registry.example").UserAgent);
        Assert.Equal(first.UserAgent, rotation.For("REGISTRY.example").UserAgent);
    }

    /// <summary>And different hosts are told apart, or the whole rotation would collapse into one browser.</summary>
    [Fact]
    public void For_DifferentHosts_AreNotAllTheSameIdentity()
    {
        var rotation = Rotation(
            new Fingerprint { UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0.0.0" },
            new Fingerprint { UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Chrome/124.0.0.0" },
            new Fingerprint { UserAgent = "Mozilla/5.0 (X11; Linux x86_64) Chrome/124.0.0.0" });

        var seen = new[] { "a.example", "b.example", "c.example", "d.example", "e.example", "f.example" }
            .Select(host => rotation.For(host).Platform)
            .Distinct()
            .Count();

        Assert.True(seen > 1, "every host was given the same identity");
    }

    /// <summary>
    /// The exit is part of the same promise: a clearance cookie is bound to the address that earned it.
    /// </summary>
    [Fact]
    public void For_TheSameHost_LeavesByTheSameExit()
    {
        var options = new IdentityOptions
        {
            Fingerprints = [new Fingerprint { UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0.0.0" }],
            Proxies = ["http://one.example:8080", "http://two.example:8080", "http://three.example:8080"]
        };

        var rotation = new IdentityRotation(
            Options.Create(options),
            new ProxyRotation(Options.Create(options)),
            NullLogger<IdentityRotation>.Instance);

        var first = rotation.For("registry.example").Proxy?.Server;

        rotation.Next();

        Assert.Equal(first, rotation.For("registry.example").Proxy?.Server);
    }

    /// <summary>
    /// An identity is shaped once per fingerprint, and shaped again when the fingerprint's values change: a
    /// fingerprint is a settings object, and what it says now is what the next identity says.
    /// </summary>
    [Fact]
    public void Next_FingerprintChangedAfterStart_SaysWhatTheFingerprintSaysNow()
    {
        var fingerprint = new Fingerprint { UserAgent = KnownAgent, AcceptLanguage = "en-US,en;q=0.9" };
        var rotation = Rotation(fingerprint);

        rotation.Next();
        fingerprint.AcceptLanguage = "fr-FR,fr;q=0.9";
        fingerprint.UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Firefox/131.0";

        var identity = rotation.Next();

        Assert.Equal(["fr-FR", "fr"], identity.Languages);
        Assert.Equal("macOS", identity.Platform);
        Assert.Equal("Firefox", identity.Brand);
    }

    /// <summary>The exit travels with the identity it was handed out with, and only with it.</summary>
    [Fact]
    public void Next_WithProxiesConfigured_GivesEachIdentityItsOwnExit()
    {
        var options = new IdentityOptions
        {
            Fingerprints = [new Fingerprint { UserAgent = KnownAgent }],
            Proxies = ["http://one.example:8080", "http://two.example:8080"]
        };
        var rotation = new IdentityRotation(
            Options.Create(options),
            new ProxyRotation(Options.Create(options)),
            NullLogger<IdentityRotation>.Instance);

        var first = rotation.Next();
        var second = rotation.Next();

        Assert.Equal("http://one.example:8080", first.Proxy!.Server);
        Assert.Equal("http://two.example:8080", second.Proxy!.Server);
        Assert.Equal(first.UserAgent, second.UserAgent);
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
