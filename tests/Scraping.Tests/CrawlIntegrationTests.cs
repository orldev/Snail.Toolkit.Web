using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Scraping.Behavior;
using Snail.Toolkit.Web.Scraping.Extensions;
using Snail.Toolkit.Web.Sitemaps;
using Xunit.Abstractions;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Drives the crawl and the two documents a site publishes about itself against real containers.
/// </summary>
/// <remarks>
/// The crawl runs the link-following path, because the browser and the test process reach the fixture site by
/// different names and only one of them can be the address a crawl starts from. The sitemap and robots
/// downloads are exercised on their own against the published port, and the junction between a sitemap and the
/// crawl is covered by <see cref="CrawlerTests"/> without a browser.
/// </remarks>
[Collection(SiteCollection.Name)]
[Trait("Category", "Integration")]
public class CrawlIntegrationTests(SiteFixture site, ITestOutputHelper output)
{
    [Fact]
    public async Task CrawlAsync_SiteOfLinkedPages_ReadsEveryPageOnce()
    {
        await using var host = Host();

        var report = await host.Crawler.CrawlAsync(Scope(host));

        Assert.Equal(CrawlStop.Exhausted, report.Stop);
        Assert.Equal(
            [Canonical("/barley.html"), Canonical("/grain.html"), Canonical("/hub.html")],
            report.Pages.Select(page => page.Value.Address.Canonical).Order(StringComparer.Ordinal));
    }

    /// <summary>A crawl answers what a site says. The markup of five hundred pages is not that answer.</summary>
    [Fact]
    public async Task CrawlAsync_PageItRead_CarriesTheTextRatherThanTheMarkup()
    {
        await using var host = Host();

        var report = await host.Crawler.CrawlAsync(Scope(host));
        var grain = report.Pages.Single(page => page.Value.Address.Canonical.EndsWith("grain.html", StringComparison.Ordinal));

        Assert.Contains("Wheat prices rose.", grain.Value.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", grain.Value.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrawlAsync_LinkOffTheSite_IsNotFollowed()
    {
        await using var host = Host();

        var report = await host.Crawler.CrawlAsync(Scope(host));

        Assert.DoesNotContain(report.Pages, page => page.Value.Address.Canonical.Contains("elsewhere", StringComparison.Ordinal));
    }

    /// <summary>
    /// The whole chain a real site puts in the way: an index pointing at a gzipped part, served with no
    /// <c>Content-Encoding</c> to announce the compression.
    /// </summary>
    [Fact]
    public async Task ReadAsync_SitemapIndexPointingAtAGzippedPart_ReadsThePages()
    {
        await using var host = Host();

        var index = await host.Sitemaps.ReadAsync(new Uri($"{site.LocalUrl}/sitemap.xml"), null);

        Assert.True(index.IsIndex);

        var part = await host.Sitemaps.ReadAsync(Local(index.Sitemaps[0]), null);

        Assert.Equal(
            [Canonical("/grain.html"), Canonical("/barley.html")],
            part.Pages.Select(page => page.Address.Canonical));
    }

    [Fact]
    public async Task ReadAsync_SitemapThatIsNotThere_ReadsAsEmpty()
    {
        await using var host = Host();

        var file = await host.Sitemaps.ReadAsync(new Uri($"{site.LocalUrl}/no-sitemap.xml"), null);

        Assert.Empty(file.Pages);
        Assert.Empty(file.Sitemaps);
    }

    /// <summary>
    /// Closes the gap that made robots a parser test only: the file is downloaded, and the directives that
    /// used to be dropped as unknown fields survive the transport.
    /// </summary>
    [Fact]
    public async Task ReadAsync_PublishedRobotsFile_CarriesItsRulesSitemapAndDelay()
    {
        await using var host = Host();

        var rules = await host.Robots.ReadAsync(new Uri(site.LocalUrl), null);

        Assert.False(rules.IsAllowed("/private/secret.html", "*"));
        Assert.Equal(Canonical("/sitemap.xml"), Assert.Single(rules.Sitemaps).Canonical);
        Assert.Equal(TimeSpan.Zero, rules.CrawlDelay("*"));
    }

    /// <summary>An address in the form links and sitemaps are compared by, which is not the form they are written in.</summary>
    private static string Canonical(string path) => Link.Of($"{SiteFixture.BaseUrl}{path}").Canonical;

    /// <summary>The sitemap names the site as the browser knows it, which is not how this process reaches it.</summary>
    private Uri Local(Link declared)
        => new($"{site.LocalUrl}{declared.Address?.AbsolutePath}");

    private static CrawlScope Scope(Harness host)
        => host.Crawler.ScopeOf(Link.Of($"{SiteFixture.BaseUrl}/hub.html"));

    private Harness Host()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddXUnit(output).SetMinimumLevel(LogLevel.Debug));
        services.AddScraping(options =>
        {
            options.BrowserServer = site.BrowserServer;
            options.MaxRetryAttempts = 0;
            options.NavigationTimeoutMs = 20_000;
        });

        services.Configure<HumanBehaviorOptions>(options => options.Enabled = false);
        services.Configure<PolitenessOptions>(options => options.MinDelayPerHost = TimeSpan.Zero);
        services.Configure<RobotsOptions>(options => options.IsObeyed = false);

        return new Harness(services.BuildServiceProvider());
    }

    private sealed class Harness(ServiceProvider provider) : IAsyncDisposable
    {
        public ICrawler Crawler { get; } = provider.GetRequiredService<ICrawler>();

        public ISitemapSource Sitemaps { get; } = provider.GetRequiredService<ISitemapSource>();

        public IRobotsSource Robots { get; } = provider.GetRequiredService<IRobotsSource>();

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
