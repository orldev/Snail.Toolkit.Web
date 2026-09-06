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

        Assert.Equal([DuckDuckGo.ProviderName, Google.ProviderName], names);
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

    /// <summary>Registration is idempotent, so a second call cannot ask Google twice per query.</summary>
    [Fact]
    public async Task AddBrowserSearch_CalledTwice_AsksEachProviderOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBrowserSearch(new ConfigurationBuilder().Build());
        services.AddBrowserSearch(new ConfigurationBuilder().Build());

        await using var provider = services.BuildServiceProvider();

        Assert.Equal(2, provider.GetRequiredService<IEnumerable<ISearchProvider>>().Count());
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
