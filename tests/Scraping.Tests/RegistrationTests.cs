using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Scraping.Behavior;
using Snail.Toolkit.Web.Scraping.Extensions;
using Snail.Toolkit.Web.Sitemaps;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers binding and validation, which previously happened by hand and not at all.
/// </summary>
public class RegistrationTests
{
    [Fact]
    public void AddScraping_ValidConfiguration_BindsEveryOptionClass()
    {
        var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Scraping:MaxDegreeOfParallelism"] = "7",
            ["Web:Scraping:Headless"] = "false",
            ["Web:Scraping:BrowserIdleTimeout"] = "00:02:00",
            ["Web:Scraping:HumanBehavior:MaxScrollDistance"] = "4242",
            ["Web:Scraping:Crawl:MaxPages"] = "42",
            ["Web:Politeness:MinDelayPerHost"] = "00:00:03"
        });

        var scraping = provider.GetRequiredService<IOptions<ScrapingOptions>>().Value;
        var behavior = provider.GetRequiredService<IOptions<HumanBehaviorOptions>>().Value;
        var crawling = provider.GetRequiredService<IOptions<CrawlOptions>>().Value;
        var politeness = provider.GetRequiredService<IOptions<PolitenessOptions>>().Value;

        Assert.Equal(7, scraping.MaxDegreeOfParallelism);
        Assert.False(scraping.Headless);
        Assert.Equal(TimeSpan.FromMinutes(2), scraping.BrowserIdleTimeout);
        Assert.Equal(4242, behavior.MaxScrollDistance);
        Assert.Equal(42, crawling.MaxPages);
        Assert.Equal(TimeSpan.FromSeconds(3), politeness.MinDelayPerHost);
    }

    /// <summary>
    /// The behaviour settings used to be a hard-coded instance in the registration, so configuration could not
    /// reach them and the defaults declared on the class were silently overridden.
    /// </summary>
    [Fact]
    public void AddScraping_HumanBehaviorSection_IsReachableFromConfiguration()
    {
        var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Scraping:HumanBehavior:Enabled"] = "false",
            ["Web:Scraping:HumanBehavior:MouseSteps"] = "9"
        });

        var behavior = provider.GetRequiredService<IOptions<HumanBehaviorOptions>>().Value;

        Assert.False(behavior.Enabled);
        Assert.Equal(9, behavior.MouseSteps);
    }

    [Fact]
    public void AddScraping_NonPositiveParallelism_FailsValidation()
    {
        var provider = Built(new Dictionary<string, string?> { ["Web:Scraping:MaxDegreeOfParallelism"] = "0" });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ScrapingOptions>>().Value);
    }

    /// <summary>
    /// A malformed proxy used to throw from the middle of a fetch. The setting now lives in the core's section
    /// and is refused before the host starts.
    /// </summary>
    [Fact]
    public void AddScraping_MalformedProxy_FailsValidation()
    {
        var provider = Built(new Dictionary<string, string?> { ["Web:Identity:Proxies:0"] = "definitely not a proxy" });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<Web.Identity.IdentityOptions>>().Value);
    }

    /// <summary>
    /// A proxy address was once configured as the browser server, because the setting was called <c>BaseUrl</c>
    /// and read like one. The name now says what it is and validation refuses the mistake.
    /// </summary>
    [Fact]
    public void AddScraping_ProxyAddressAsBrowserServer_FailsValidation()
    {
        var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Scraping:BrowserServer"] = "http://user:pass@23.95.150.145:6114"
        });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ScrapingOptions>>().Value);
    }

    [Fact]
    public void AddScraping_WebSocketBrowserServer_PassesValidation()
    {
        var provider = Built(new Dictionary<string, string?> { ["Web:Scraping:BrowserServer"] = "ws://localhost:3000/" });

        Assert.Equal("ws://localhost:3000/", provider.GetRequiredService<IOptions<ScrapingOptions>>().Value.BrowserServer);
    }

    /// <summary>Registration is idempotent, so a second call cannot double every dependency.</summary>
    [Fact]
    public void AddScraping_CalledTwice_RegistersOneOfEach()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddScraping(configuration);
        services.AddScraping(configuration);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPageReader));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ICrawler));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISiteRules));
    }

    /// <summary>Everything is a singleton, matching the lifetime the rest of the toolkit uses.</summary>
    [Fact]
    public void AddScraping_Readers_AreSingletons()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScraping(new ConfigurationBuilder().Build());

        var reader = services.Single(descriptor => descriptor.ServiceType == typeof(IPageReader));

        Assert.Equal(ServiceLifetime.Singleton, reader.Lifetime);
    }

    /// <summary>
    /// Robots rules resolve here and not from the core alone: obeying them needs a transport, and this is the
    /// package that brings one.
    /// </summary>
    [Fact]
    public void AddScraping_RobotsRules_ResolveThroughThisPackagesTransport()
    {
        using var provider = Built([]);

        Assert.NotNull(provider.GetRequiredService<ISiteRules>());
        Assert.IsType<Robots.HttpRobotsSource>(provider.GetRequiredService<IRobotsSource>());
    }

    /// <summary>A sitemap is the other document a crawl needs and a browser is the wrong way to read.</summary>
    [Fact]
    public void AddScraping_SitemapSource_ResolvesThroughThisPackagesTransport()
    {
        using var provider = Built([]);

        Assert.IsType<Sitemaps.HttpSitemapSource>(provider.GetRequiredService<ISitemapSource>());
    }

    [Fact]
    public void AddScraping_CrawlCeilingThatCannotWork_FailsValidation()
    {
        var provider = Built(new Dictionary<string, string?> { ["Web:Scraping:Crawl:MaxPages"] = "0" });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<CrawlOptions>>().Value);
    }

    /// <summary>
    /// Disposed with <c>await using</c> deliberately: the browser pool is <see cref="IAsyncDisposable"/> only,
    /// and a container disposed synchronously refuses to close it rather than blocking on a browser shutdown.
    /// A consumer that holds the provider has to do the same.
    /// </summary>
    [Fact]
    public async Task AddScraping_ConfiguredInCode_ResolvesTheSameParts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScraping(options => options.MaxBrowsers = 4);

        await using var provider = services.BuildServiceProvider();

        Assert.Equal(4, provider.GetRequiredService<IOptions<ScrapingOptions>>().Value.MaxBrowsers);
        Assert.NotNull(provider.GetRequiredService<ICrawler>());
    }

    private static ServiceProvider Built(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddScraping(configuration);

        return services.BuildServiceProvider();
    }
}
