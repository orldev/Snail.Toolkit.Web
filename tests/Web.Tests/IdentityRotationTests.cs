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

    private static IdentityRotation Rotation(params Fingerprint[] fingerprints)
        => new(
            Options.Create(new IdentityOptions { Fingerprints = [.. fingerprints] }),
            new ProxyRotation(Options.Create(new IdentityOptions())),
            NullLogger<IdentityRotation>.Instance);
}
