using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Search.Browser.Extensions;
using Snail.Toolkit.Web.Search.Providers;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// Covers the composition: browser search is HTTP search plus a page reader, and both halves have to be there.
/// </summary>
public class RegistrationTests
{
    [Fact]
    public async Task AddBrowserSearch_ProviderSet_HoldsBothTheHttpAndTheBrowserProvider()
    {
        await using var provider = Built([]);

        var names = provider.GetRequiredService<IEnumerable<ISearchProvider>>().Select(p => p.Name).Order();

        Assert.Equal([Bing.ProviderName, Brave.ProviderName, DuckDuckGo.ProviderName], names);
    }

    [Fact]
    public async Task AddBrowserSearch_PageReader_IsRegisteredForTheBrowserProviderToUse()
    {
        await using var provider = Built([]);

        Assert.NotNull(provider.GetRequiredService<IPageReader>());
    }

    /// <summary>Registration is idempotent, so a second call cannot ask an engine twice per query.</summary>
    [Fact]
    public async Task AddBrowserSearch_CalledTwice_AsksEachProviderOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBrowserSearch(new ConfigurationBuilder().Build());
        services.AddBrowserSearch(new ConfigurationBuilder().Build());

        await using var provider = services.BuildServiceProvider();

        Assert.Equal(3, provider.GetRequiredService<IEnumerable<ISearchProvider>>().Count());
    }

    /// <summary>Registration is idempotent in its settings too, not only in its services.</summary>
    /// <remarks>
    /// Browser search is HTTP search plus a page reader, so one call reaches the core's registration twice and
    /// binds its sections twice. Binding appends to a list rather than replacing it: measured on 2026-09-13,
    /// two configured proxies came back as four and a Chromium switch was passed twice. The test above covers
    /// the services, which <c>TryAdd</c> already guarded — this one covers the half that was not guarded.
    /// </remarks>
    [Fact]
    public async Task AddBrowserSearch_ReachingTheCoreThroughBothHalves_BindsTheIdentityListOnce()
    {
        await using var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Identity:Proxies:0"] = "http://one.example:8080",
            ["Web:Identity:Proxies:1"] = "http://two.example:8080"
        });

        var identity = provider.GetRequiredService<IOptions<Identity.IdentityOptions>>().Value;

        Assert.Equal(["http://one.example:8080", "http://two.example:8080"], identity.Proxies);
    }

    /// <summary>
    /// The second browser engine is configured and validated the same way as the first.
    /// </summary>
    /// <remarks>
    /// Added because which engine answers a given client from a given address is a fact that changes:
    /// measured on 2026-09-12 from a datacentre address, Brave answered with a hundred links across
    /// twenty-six sites while Google returned a captcha. A setting that cannot work has to refuse at start-up
    /// rather than produce an address like <c>https://https://search.brave.com/search</c> far from its cause.
    /// </remarks>
    [Theory]
    [InlineData("https://search.brave.com")]
    [InlineData("search.brave.com/search")]
    [InlineData("")]
    public async Task AddBrowserSearch_BraveDomainThatIsNotAHost_FailsValidation(string domain)
    {
        await using var provider = Built(new Dictionary<string, string?> { ["Web:Search:Brave:Domain"] = domain });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<BraveOptions>>().Value);
    }

    /// <summary>An engine added on its own has to bring what it needs to be built.</summary>
    /// <remarks>
    /// Every other check here goes through <c>AddBrowserSearch</c>, which is how this went unnoticed: until
    /// 2026-09-14 <c>AddBrave</c> registered the provider and nothing else, so a deployment that took its own
    /// remark at its word got a container resolving <see cref="Brave"/> into an <see cref="IPageReader"/>
    /// nobody had registered. That failure arrives on the first query rather than at startup, because
    /// validation checks settings and not the graph — so this asks for the providers themselves, which is what
    /// forces them to be built.
    /// </remarks>
    [Fact]
    public async Task AddBrave_OnItsOwn_BuildsAProviderThatCanBeAsked()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBrave(new ConfigurationBuilder().Build());

        await using var provider = services.BuildServiceProvider();

        Assert.Contains(
            provider.GetRequiredService<IEnumerable<ISearchProvider>>(),
            engine => engine.Name == Brave.ProviderName);
    }

    private static ServiceProvider Built(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddBrowserSearch(configuration);

        return services.BuildServiceProvider();
    }
}
