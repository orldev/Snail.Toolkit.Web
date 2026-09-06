using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Search.Extensions;
using Snail.Toolkit.Web.Search.Providers;

namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// Covers binding, validation and the composition of the provider set.
/// </summary>
public class RegistrationTests
{
    [Fact]
    public void AddSearch_ValidConfiguration_BindsItsOwnAndTheCoreSections()
    {
        using var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Search:MaxDegreeOfParallelism"] = "9",
            ["Web:Search:MaxResultsPerProvider"] = "5",
            ["Web:Politeness:MinDelayPerHost"] = "00:00:03"
        });

        var search = provider.GetRequiredService<IOptions<SearchOptions>>().Value;

        Assert.Equal(9, search.MaxDegreeOfParallelism);
        Assert.Equal(5, search.MaxResultsPerProvider);
        Assert.Equal(
            TimeSpan.FromSeconds(3),
            provider.GetRequiredService<IOptions<PolitenessOptions>>().Value.MinDelayPerHost);
    }

    [Theory]
    [InlineData("Web:Search:MaxDegreeOfParallelism", "0")]
    [InlineData("Web:Search:MaxResultsPerProvider", "0")]
    [InlineData("Web:Search:Timeout", "00:00:00")]
    [InlineData("Web:Search:Timeout", "00:00:00.500")]
    public void AddSearch_SettingThatCannotWork_FailsValidation(string key, string value)
    {
        using var provider = Built(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SearchOptions>>().Value);
    }

    [Fact]
    public void AddSearch_ByDefault_RegistersTheHtmlProvider()
    {
        using var provider = Built([]);

        Assert.IsType<DuckDuckGo>(Assert.Single(provider.GetRequiredService<IEnumerable<ISearchProvider>>()));
    }

    /// <summary>Providers accumulate rather than replace each other, which is what an enumerable is for.</summary>
    [Fact]
    public void AddSearchProvider_AnotherProvider_JoinsTheSet()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSearch(new ConfigurationBuilder().Build());
        services.AddSearchProvider<Elsewhere>();

        using var provider = services.BuildServiceProvider();

        Assert.Equal(2, provider.GetRequiredService<IEnumerable<ISearchProvider>>().Count());
    }

    /// <summary>Registration is idempotent, so a second call cannot ask the same back end twice per query.</summary>
    [Fact]
    public void AddSearch_CalledTwice_AsksEachProviderOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSearch(new ConfigurationBuilder().Build());
        services.AddSearch(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetRequiredService<IEnumerable<ISearchProvider>>());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWebSearch));
    }

    [Fact]
    public void AddSearch_NamedClient_IsBuildableForEveryProvider()
    {
        using var provider = Built([]);

        var clients = provider.GetRequiredService<IHttpClientFactory>();

        Assert.NotNull(clients.CreateClient(DuckDuckGo.ProviderName));
    }

    /// <summary>
    /// The standard resilience handler disables the client's own timeout so its total-request timeout owns the
    /// clock. A configured <see cref="HttpClient.Timeout"/> beside it would be a setting that silently does
    /// nothing — which is exactly what this registration must not ship.
    /// </summary>
    [Fact]
    public void AddSearch_Client_LeavesTheDeadlineToTheResiliencePipeline()
    {
        using var provider = Built(new Dictionary<string, string?> { ["Web:Search:Timeout"] = "00:00:07" });

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(DuckDuckGo.ProviderName);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void AddSearch_ConfiguredInCode_ResolvesTheSameParts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSearch(options => options.MaxResultsPerProvider = 3);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(3, provider.GetRequiredService<IOptions<SearchOptions>>().Value.MaxResultsPerProvider);
        Assert.NotNull(provider.GetRequiredService<IWebSearch>());
    }

    /// <summary>
    /// The search package must stay usable without a browser: that is the entire reason the browser-backed
    /// providers live in a companion package.
    /// </summary>
    [Fact]
    public void AddSearch_LoadedAssemblies_DoNotIncludePlaywright()
    {
        using var provider = Built([]);

        provider.GetRequiredService<IWebSearch>();

        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly => assembly.GetName().Name?.StartsWith("Microsoft.Playwright", StringComparison.Ordinal) is true);
    }

    private static ServiceProvider Built(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSearch(configuration);

        return services.BuildServiceProvider();
    }

    private sealed class Elsewhere : ISearchProvider
    {
        public string Name => "Elsewhere";

        public Task<IReadOnlyList<SearchResult>> FindAsync(string query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SearchResult>>([]);
    }
}
