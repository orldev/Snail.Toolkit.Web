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
            ["Web:Search:RefusalCooldown"] = "00:02:00",
            ["Web:Search:ProvidersPerQuery"] = "3",
            ["Web:Search:AlwaysAsked:0"] = "corpus",
            ["Web:Politeness:MinDelayPerHost"] = "00:00:03"
        });

        var search = provider.GetRequiredService<IOptions<SearchOptions>>().Value;

        Assert.Equal(9, search.MaxDegreeOfParallelism);
        Assert.Equal(5, search.MaxResultsPerProvider);
        Assert.Equal(TimeSpan.FromMinutes(2), search.RefusalCooldown);
        Assert.Equal(3, search.ProvidersPerQuery);
        Assert.Equal(["corpus"], search.AlwaysAsked);
        Assert.Equal(
            TimeSpan.FromSeconds(3),
            provider.GetRequiredService<IOptions<PolitenessOptions>>().Value.MinDelayPerHost);
    }

    [Theory]
    [InlineData("Web:Search:MaxDegreeOfParallelism", "0")]
    [InlineData("Web:Search:MaxResultsPerProvider", "0")]
    [InlineData("Web:Search:Timeout", "00:00:00")]
    [InlineData("Web:Search:Timeout", "00:00:00.500")]
    [InlineData("Web:Search:RefusalCooldown", "00:00:00")]
    [InlineData("Web:Search:MaxRefusalCooldown", "00:01:00")]
    [InlineData("Web:Search:ProvidersPerQuery", "0")]
    public void AddSearch_SettingThatCannotWork_FailsValidation(string key, string value)
    {
        using var provider = Built(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SearchOptions>>().Value);
    }

    /// <summary>
    /// Two engines answer a plain request, and both are registered.
    /// </summary>
    /// <remarks>
    /// Measured on 2026-09-12 from a datacentre address: engines disagree about who they will answer, and one
    /// of them refused where the other returned results. A set of one makes the search as available as its
    /// weakest day, which is the whole reason the providers are an enumerable.
    /// </remarks>
    [Fact]
    public void AddSearch_ByDefault_RegistersBothHtmlProviders()
    {
        using var provider = Built([]);

        var names = provider.GetRequiredService<IEnumerable<ISearchProvider>>()
            .Select(engine => engine.Name)
            .Order();

        Assert.Equal([Bing.ProviderName, DuckDuckGo.ProviderName], names);
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

        Assert.Equal(3, provider.GetRequiredService<IEnumerable<ISearchProvider>>().Count());
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

        Assert.Equal(2, provider.GetRequiredService<IEnumerable<ISearchProvider>>().Count());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWebSearch));
    }

    /// <summary>A rest recorded by one search has to hold for the next, which takes one memory per process.</summary>
    [Fact]
    public void AddSearch_MemoryOfRefusals_IsOnePerProcess()
    {
        using var provider = Built([]);

        provider.GetRequiredService<ISearchCooldown>().Refused(DuckDuckGo.ProviderName);

        Assert.True(provider.GetRequiredService<ISearchCooldown>().IsResting(DuckDuckGo.ProviderName));
    }

    [Fact]
    public void AddSearch_NamedClient_IsBuildableForEveryProvider()
    {
        using var provider = Built([]);

        var clients = provider.GetRequiredService<IHttpClientFactory>();

        Assert.NotNull(clients.CreateClient(DuckDuckGo.ProviderName));
        Assert.NotNull(clients.CreateClient(Bing.ProviderName));
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

        public Task<SearchAnswer> FindAsync(string query, CancellationToken ct = default)
            => Task.FromResult(SearchAnswer.Success([]));
    }
}
