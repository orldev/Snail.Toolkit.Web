using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Sitemaps;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers reading a list of addresses and reading a whole site, against a stub reader and no browser.
/// </summary>
public class CrawlerTests
{
    private const string Site = "https://example.com";

    private const string Home = $"{Site}/";

    /// <summary>
    /// Regression for the leak that made a browser unreleasable. The producer wrote into a bounded channel and
    /// the <c>await</c> on it sat after the consumer loop, unreachable on an early exit, so a consumer that
    /// stopped reading left the producer blocked forever while holding a browser lease.
    /// </summary>
    [Fact]
    public async Task StreamAsync_ConsumerStopsEarly_ProducerStops()
    {
        var reader = new StubReader(Ok);
        var crawler = Crawler(reader, parallelism: 2);

        await foreach (var _ in crawler.StreamAsync(Addresses(200)))
            break;

        var afterBreak = reader.Calls;
        await Task.Delay(250);

        Assert.Equal(afterBreak, reader.Calls);
        Assert.True(afterBreak < 200, $"the producer ran to completion ({afterBreak} of 200)");
    }

    /// <summary>One unreachable site used to cancel the whole batch through Parallel.ForEachAsync.</summary>
    [Fact]
    public async Task ReadAllAsync_OneAddressFails_TheOthersStillReturn()
    {
        var crawler = Crawler(new StubReader(address => address.Text.Contains("bad", StringComparison.Ordinal)
            ? Unreachable(address)
            : Ok(address)));

        var results = await crawler.ReadAllAsync(
            [Link.Of("https://a.example/"), Link.Of("https://bad.example/"), Link.Of("https://c.example/")]);

        Assert.Equal(3, results.Count);
        Assert.Equal(2, results.Count(outcome => outcome.IsSuccess));
        Assert.Single(results, outcome => !outcome.IsSuccess);
    }

    /// <summary>
    /// Every outcome names its own address, on both branches, so nothing has to be matched back to the input by
    /// position — which is why the wrapper type the previous crawler returned is gone.
    /// </summary>
    [Fact]
    public async Task ReadAllAsync_EveryAddress_CarriesItsOwnOutcome()
    {
        var crawler = Crawler(new StubReader(Ok));

        var results = await crawler.ReadAllAsync([Link.Of("https://a.example/"), Link.Of("https://b.example/")]);

        Assert.Equal(
            ["https://a.example/", "https://b.example/"],
            results.Select(outcome => outcome.Value.Requested.Canonical).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReadAllAsync_NoAddresses_ReturnsNothing()
        => Assert.Empty(await Crawler(new StubReader(Ok)).ReadAllAsync([]));

    [Fact]
    public async Task ReadAllAsync_CancelledToken_Throws()
    {
        var crawler = Crawler(new StubReader(Ok), parallelism: 2);
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => crawler.ReadAllAsync(Addresses(50), cancellation.Token));
    }

    /// <summary>
    /// The whole point of asking first: a site that lists its own pages is read from that list, which reaches
    /// pages nothing links to and costs one request per page instead of one per page plus the walk.
    /// </summary>
    [Fact]
    public async Task CrawlAsync_SiteWithASitemap_ReadsTheListedPages()
    {
        var crawler = Crawler(new StubReader(Linked("<a href='/nowhere'>x</a>")), sitemap: Urlset("/grain", "/barley"));

        var report = await crawler.CrawlAsync(Scope(crawler));

        Assert.True(report.IsFromSitemap);
        Assert.Equal(CrawlStop.Exhausted, report.Stop);
        Assert.Equal(
            [Home, $"{Site}/barley", $"{Site}/grain"],
            report.Pages.Select(page => page.Value.Address.Canonical).Order(StringComparer.Ordinal));
    }

    /// <summary>Large sites split the sitemap and publish an index of the parts.</summary>
    [Fact]
    public async Task CrawlAsync_SitemapIndex_ReadsThePagesOfEveryPart()
    {
        var crawler = Crawler(
            new StubReader(Ok),
            sitemap: address => address.AbsolutePath switch
            {
                "/sitemap.xml" => SitemapFile.Parse(
                    $"<sitemapindex><sitemap><loc>{Site}/pages.xml</loc></sitemap></sitemapindex>"),
                "/pages.xml" => SitemapFile.Parse($"<urlset><url><loc>{Site}/grain</loc></url></urlset>"),
                _ => SitemapFile.Empty
            });

        var report = await crawler.CrawlAsync(Scope(crawler));

        Assert.Contains(report.Pages, page => page.Value.Address.Canonical == $"{Site}/grain");
    }

    [Fact]
    public async Task CrawlAsync_SiteWithoutASitemap_FollowsItsLinks()
    {
        var crawler = Crawler(new StubReader(Linked("<a href='/grain'>grain</a>")));

        var report = await crawler.CrawlAsync(Scope(crawler));

        Assert.False(report.IsFromSitemap);
        Assert.Contains(report.Pages, page => page.Value.Address.Canonical == $"{Site}/grain");
    }

    /// <summary>Depth is what a caller reads to tell the front page from something four clicks in.</summary>
    [Fact]
    public async Task CrawlAsync_FollowedLink_RecordsHowDeepItWasFound()
    {
        var crawler = Crawler(new StubReader(address => address.Canonical == Home
            ? Ok(address, "<a href='/grain'>grain</a>")
            : Ok(address)));

        var report = await crawler.CrawlAsync(Scope(crawler));

        Assert.Equal(0, Page(report, Home).Depth);
        Assert.Equal(1, Page(report, $"{Site}/grain").Depth);
    }

    [Fact]
    public async Task CrawlAsync_LinkToAnotherSite_IsNotRead()
    {
        var crawler = Crawler(new StubReader(Linked("<a href='https://elsewhere.example/grain'>grain</a>")));

        var report = await crawler.CrawlAsync(Scope(crawler));

        Assert.Single(report.Pages);
    }

    /// <summary>Off by default, on for a caller sweeping everything a company publishes.</summary>
    [Fact]
    public async Task CrawlAsync_SubdomainLink_IsReadOnlyWhenSubdomainsAreIncluded()
    {
        var crawler = Crawler(new StubReader(Linked("<a href='https://docs.example.com/guide'>guide</a>")));

        var narrow = await crawler.CrawlAsync(Scope(crawler));
        var wide = await crawler.CrawlAsync(Scope(crawler) with { IncludesSubdomains = true });

        Assert.Single(narrow.Pages);
        Assert.Equal(2, wide.Pages.Count);
    }

    [Fact]
    public async Task CrawlAsync_PageCeilingReached_StopsAndSaysSo()
    {
        var crawler = Crawler(new StubReader(Linked("<a href='/a'>a</a><a href='/b'>b</a><a href='/c'>c</a>")));

        var report = await crawler.CrawlAsync(Scope(crawler) with { MaxPages = 2 });

        Assert.Equal(2, report.Pages.Count);
        Assert.Equal(CrawlStop.PageLimit, report.Stop);
    }

    [Fact]
    public async Task CrawlAsync_DepthCeilingReached_StopsAndSaysSo()
    {
        var crawler = Crawler(new StubReader(Linked("<a href='/deeper'>deeper</a>")));

        var report = await crawler.CrawlAsync(Scope(crawler) with { MaxDepth = 0 });

        Assert.Single(report.Pages);
        Assert.Equal(CrawlStop.DepthLimit, report.Stop);
    }

    /// <summary>Two pages that link to each other are the ordinary shape of a site, not a trap to fall into.</summary>
    [Fact]
    public async Task CrawlAsync_PagesLinkingToEachOther_AreEachReadOnce()
    {
        var crawler = Crawler(new StubReader(address => address.Canonical == Home
            ? Ok(address, "<a href='/grain'>grain</a>")
            : Ok(address, "<a href='/'>home</a>")));

        var report = await crawler.CrawlAsync(Scope(crawler));

        Assert.Equal(2, report.Pages.Count);
    }

    /// <summary>
    /// The same document served at two addresses is one page. Reducing the forms of an address cannot see
    /// that; only the document saying which address is its own can.
    /// </summary>
    [Fact]
    public async Task CrawlAsync_OneDocumentAtTwoAddresses_IsReportedOnce()
    {
        var crawler = Crawler(new StubReader(address => address.Canonical == Home
            ? Ok(address, "<a href='/grain'>grain</a><a href='/grain?print=1'>print</a>")
            : Ok(address, $"<link rel='canonical' href='{Site}/grain'>")));

        var report = await crawler.CrawlAsync(Scope(crawler));

        Assert.Equal(2, report.Pages.Count);
        Assert.Single(report.Pages, page => page.Value.Address.Canonical == $"{Site}/grain");
    }

    /// <summary>
    /// Measured on a live static site: <c>/</c> and <c>/index.html</c> are one document served twice, and
    /// nothing on either page says so. Identical text is then the only evidence there is.
    /// </summary>
    [Fact]
    public async Task CrawlAsync_TwoAddressesSayingExactlyTheSame_IsReportedOnce()
    {
        var crawler = Crawler(new StubReader(address => address.Canonical == Home
            ? Read(address, "<a href='/index.html'>home again</a>", new string('a', 500))
            : Read(address, "<p>x</p>", new string('a', 500))));

        var report = await crawler.CrawlAsync(Scope(crawler));

        Assert.Single(report.Pages);
    }

    /// <summary>
    /// Two thin pages are identical for reasons that have nothing to do with being one document, so the text
    /// has to be long enough to be its own evidence before it counts as one.
    /// </summary>
    [Fact]
    public async Task CrawlAsync_TwoThinPagesThatMatch_AreBothKept()
    {
        var crawler = Crawler(new StubReader(address => address.Canonical == Home
            ? Read(address, "<a href='/empty'>empty</a>", "nothing here")
            : Read(address, "<p>x</p>", "nothing here")));

        var report = await crawler.CrawlAsync(Scope(crawler));

        Assert.Equal(2, report.Pages.Count);
    }

    [Fact]
    public async Task CrawlAsync_PageThatFails_IsReportedWithoutStoppingTheCrawl()
    {
        var crawler = Crawler(new StubReader(address => address.Canonical == Home
            ? Ok(address, "<a href='/gone'>gone</a><a href='/grain'>grain</a>")
            : address.Canonical.EndsWith("/gone", StringComparison.Ordinal)
                ? Unreachable(address)
                : Ok(address)));

        var report = await crawler.CrawlAsync(Scope(crawler));

        Assert.Equal(2, report.Read);
        Assert.Equal(1, report.Failed);
        Assert.Equal(CrawlStop.Exhausted, report.Stop);
    }

    /// <summary>
    /// The crawl's own budget is far tighter than a single fetch's: it multiplies by the number of pages, and
    /// a generous ceiling fills an agent's context somewhere around the twentieth page.
    /// </summary>
    [Fact]
    public async Task CrawlAsync_PageOverTheCrawlBudget_IsCutAndSaysSo()
    {
        var crawler = Crawler(
            new StubReader(address => Read(address, "<p>wheat and barley and rye</p>", "wheat and barley and rye")),
            text: 10);

        var report = await crawler.CrawlAsync(Scope(crawler));
        var page = Page(report, Home);

        Assert.True(page.IsTruncated);
        Assert.Contains(ContentBudget.Marker, page.Text, StringComparison.Ordinal);
        Assert.True(page.OriginalLength > 10);
    }

    [Fact]
    public async Task CrawlAsync_StartThatIsNotAnAddress_RefusesRatherThanReadingNothing()
    {
        var crawler = Crawler(new StubReader(Ok));

        var report = await crawler.CrawlAsync(crawler.ScopeOf(Link.Of("not an address")));

        Assert.Equal(CrawlStop.Refused, report.Stop);
        Assert.Equal(FailureKind.Navigation, Assert.Single(report.Pages).Failure?.Kind);
    }

    /// <summary>
    /// The page's name is the one thing kept from the head: an index built over a crawl has to name what it
    /// found, and the first line of the text is not the name of the page.
    /// </summary>
    [Fact]
    public async Task CrawlAsync_PageWithATitle_KeepsIt()
    {
        var crawler = Crawler(new StubReader(address => Named(address, "Wheat prices")));

        var report = await crawler.CrawlAsync(Scope(crawler));

        Assert.Equal("Wheat prices", Page(report, Home).Title);
    }

    private static CrawlScope Scope(ICrawler crawler) => crawler.ScopeOf(Link.Of(Site));

    private static CrawlPage Page(CrawlReport report, string canonical)
        => report.Pages.Single(page => page.IsSuccess && page.Value.Address.Canonical == canonical).Value;

    private static PageOutcome Ok(Link address) => Ok(address, "<html><body>ok</body></html>");

    private static PageOutcome Ok(Link address, string html) => Read(address, html, "ok");

    private static PageOutcome Read(Link address, string html, string text)
        => PageOutcome.Success(new PageContent(address, html, text, DateTimeOffset.UtcNow));

    private static PageOutcome Named(Link address, string title)
        => PageOutcome.Success(
            new PageContent(address, "<html><body>ok</body></html>", "ok", DateTimeOffset.UtcNow) { Title = title });

    private static PageOutcome Unreachable(Link address)
        => PageOutcome.Failed(new PageFailure(FailureKind.Navigation, "unreachable") { Address = address });

    private static Func<Link, PageOutcome> Linked(string html) => address => Ok(address, html);

    private static Func<Uri, SitemapFile> Urlset(params string[] paths)
        => _ => SitemapFile.Parse(
            $"<urlset>{string.Concat(paths.Select(path => $"<url><loc>{Site}{path}</loc></url>"))}</urlset>");

    private static Link[] Addresses(int count)
        => [.. Enumerable.Range(0, count).Select(index => Link.Of($"{Site}/{index}"))];

    private static Crawler Crawler(
        IPageReader reader,
        int parallelism = 3,
        Func<Uri, SitemapFile>? sitemap = null,
        int text = 0)
        => new(
            reader,
            new StubRules(),
            new StubSitemaps(sitemap ?? (_ => SitemapFile.Empty)),
            new LinkExtraction(),
            new StubIdentities(),
            Options.Create(new ScrapingOptions { MaxDegreeOfParallelism = parallelism }),
            Options.Create(new CrawlOptions { MaxTextCharacters = text }),
            NullLogger<Crawler>.Instance);

    private sealed class StubReader(Func<Link, PageOutcome> answer) : IPageReader
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async Task<PageOutcome> ReadAsync(Link address, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);

            await Task.Delay(20, ct);

            return answer(address);
        }
    }

    private sealed class StubRules : ISiteRules
    {
        public ValueTask<bool> IsAllowedAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
            => ValueTask.FromResult(true);

        public ValueTask<TimeSpan?> DelayAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
            => ValueTask.FromResult<TimeSpan?>(null);

        public ValueTask<RobotsFile> RulesAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
            => ValueTask.FromResult(RobotsFile.AllowAll);
    }

    private sealed class StubSitemaps(Func<Uri, SitemapFile> answer) : ISitemapSource
    {
        public Task<SitemapFile> ReadAsync(Uri address, ProxyEndpoint? proxy, CancellationToken ct = default)
            => Task.FromResult(answer(address));
    }

    private sealed class StubIdentities : IIdentityRotation
    {
        public WebIdentity Next()
            => new()
            {
                UserAgent = "stub",
                Platform = "macOS",
                NavigatorPlatform = "MacIntel",
                Brand = "Google Chrome",
                MajorVersion = "124",
                Locale = "en-US",
                Timezone = "UTC",
                AcceptLanguage = "en-US,en;q=0.9",
                Languages = ["en-US", "en"]
            };
    }
}
