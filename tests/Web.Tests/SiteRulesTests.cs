using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Robots;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the caching that keeps one read of <c>robots.txt</c> per origin.
/// </summary>
public class SiteRulesTests
{
    private const string Closed = """
        User-agent: *
        Disallow: /private
        """;

    [Fact]
    public async Task IsAllowedAsync_RulesNotObeyed_AllowsEverythingWithoutReading()
    {
        var source = new CountedSource(RobotsFile.Parse(Closed));
        var rules = Rules(source, isObeyed: false);

        Assert.True(await rules.IsAllowedAsync(new Uri("https://example.com/private/x"), null));
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task IsAllowedAsync_DisallowedPath_Refuses()
    {
        var rules = Rules(new CountedSource(RobotsFile.Parse(Closed)), isObeyed: true);

        Assert.False(await rules.IsAllowedAsync(new Uri("https://example.com/private/x"), null));
        Assert.True(await rules.IsAllowedAsync(new Uri("https://example.com/public/x"), null));
    }

    /// <summary>
    /// A crawl that re-read <c>robots.txt</c> per URL would announce itself twice per page, which is the
    /// opposite of what obeying the file is for.
    /// </summary>
    [Fact]
    public async Task IsAllowedAsync_ManyUrlsOfOneOrigin_ReadsTheFileOnce()
    {
        var source = new CountedSource(RobotsFile.Parse(Closed));
        var rules = Rules(source, isObeyed: true);

        foreach (var path in (string[])["/a", "/b", "/c"])
            await rules.IsAllowedAsync(new Uri($"https://example.com{path}"), null);

        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task IsAllowedAsync_DifferentOrigins_AreReadSeparately()
    {
        var source = new CountedSource(RobotsFile.Parse(Closed));
        var rules = Rules(source, isObeyed: true);

        await rules.IsAllowedAsync(new Uri("https://first.example/a"), null);
        await rules.IsAllowedAsync(new Uri("https://second.example/a"), null);

        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public async Task DelayAsync_SiteAsksForAGap_AnswersIt()
    {
        var rules = Rules(new CountedSource(RobotsFile.Parse($"{Closed}\nCrawl-delay: 3")), isObeyed: true);

        Assert.Equal(TimeSpan.FromSeconds(3), await rules.DelayAsync(new Uri("https://example.com/a"), null));
    }

    /// <summary>
    /// Switching the rules off is also a decision not to pay for the traffic that reading them costs, so the
    /// pace question answers without a fetch rather than answering from a file nobody asked for.
    /// </summary>
    [Fact]
    public async Task DelayAsync_RulesNotObeyed_AnswersNothingWithoutReading()
    {
        var source = new CountedSource(RobotsFile.Parse($"{Closed}\nCrawl-delay: 3"));
        var rules = Rules(source, isObeyed: false);

        Assert.Null(await rules.DelayAsync(new Uri("https://example.com/a"), null));
        Assert.Equal(0, source.Reads);
    }

    /// <summary>
    /// The sitemaps are read even where the rules are not obeyed: permission and a site's own index of its
    /// pages are different questions, and a crawl wants the second either way.
    /// </summary>
    [Fact]
    public async Task RulesAsync_RulesNotObeyed_StillReadsTheFileForItsSitemaps()
    {
        var source = new CountedSource(RobotsFile.Parse($"{Closed}\nSitemap: https://example.com/sitemap.xml"));
        var rules = Rules(source, isObeyed: false);

        var published = await rules.RulesAsync(new Uri("https://example.com/a"), null);

        Assert.Single(published.Sitemaps);
        Assert.Equal(1, source.Reads);
    }

    private static SiteRules Rules(IRobotsSource source, bool isObeyed)
        => new(source, Options.Create(new RobotsOptions { IsObeyed = isObeyed }));

    private sealed class CountedSource(RobotsFile rules) : IRobotsSource
    {
        private int _reads;

        public int Reads => _reads;

        public Task<RobotsFile> ReadAsync(Uri origin, ProxyEndpoint? proxy, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reads);

            return Task.FromResult(rules);
        }
    }
}
