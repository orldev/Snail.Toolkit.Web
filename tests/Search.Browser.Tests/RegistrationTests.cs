using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        Assert.Equal([Brave.ProviderName, DuckDuckGo.ProviderName, Google.ProviderName], names);
    }

    [Fact]
    public async Task AddBrowserSearch_PageReader_IsRegisteredForTheBrowserProviderToUse()
    {
        await using var provider = Built([]);

        Assert.NotNull(provider.GetRequiredService<IPageReader>());
    }

    [Fact]
    public async Task AddBrowserSearch_Domain_ComesFromConfiguration()
    {
        await using var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Search:Google:Domain"] = "www.google.de"
        });

        Assert.Equal("www.google.de", provider.GetRequiredService<IOptions<GoogleOptions>>().Value.Domain);
    }

    /// <summary>
    /// A domain written as a URL is the mistake this setting invites, and it produces an address like
    /// <c>https://https://www.google.de/search</c> that fails far from its cause.
    /// </summary>
    [Theory]
    [InlineData("https://www.google.de")]
    [InlineData("www.google.de/search")]
    [InlineData("")]
    public async Task AddBrowserSearch_DomainThatIsNotAHost_FailsValidation(string domain)
    {
        await using var provider = Built(new Dictionary<string, string?> { ["Web:Search:Google:Domain"] = domain });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<GoogleOptions>>().Value);
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

    private static ServiceProvider Built(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddBrowserSearch(configuration);

        return services.BuildServiceProvider();
    }
}
