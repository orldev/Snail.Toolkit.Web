using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Scraping.Browsers;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Drives the ceilings a caller sets on a crawl to the ends of their own types.
/// </summary>
/// <remarks>
/// <c>Crawler.Impossible</c> exists because the ceilings on a <see cref="CrawlScope"/> are the caller's to
/// choose and nothing validated at start-up covers them — a budget of zero reached
/// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> and answered a setting mistake with an
/// argument exception out of the middle of a crawl. That guard reads the bottom of each range and not the top,
/// and the top of this one is nearer than it looks.
/// </remarks>
public class CrawlCeilingTests
{
    private static readonly Link Start = Link.Of("https://example.com/");

    /// <summary>A budget longer than a timer can hold is refused the way every other bad ceiling is.</summary>
    /// <remarks>
    /// MEASURED on 2026-09-14: <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> accepts thirty days
    /// and throws <see cref="ArgumentOutOfRangeException"/> at sixty — the limit is <c>uint.MaxValue - 1</c>
    /// milliseconds, about 49.7 days. <see cref="TimeSpan.MaxValue"/> is the value a caller reaches for to mean
    /// "no limit", and it is the shortest way to write this.
    /// <para>
    /// The throw happens before the <c>try</c>, so it is not the <c>TimeLimit</c> stop and not a
    /// <c>CrawlStop.Refused</c> report: it leaves <c>CrawlAsync</c> as an exception naming a parameter called
    /// <c>delay</c>, which no caller of this API passed or has heard of. Every other impossible scope is
    /// answered with a report that says why.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(60)]
    [InlineData(365)]
    public async Task CrawlAsync_BudgetLongerThanATimerCanHold_IsRefusedRatherThanThrown(int days)
    {
        var crawler = Crawler();

        var report = await crawler.CrawlAsync(Scope() with { Budget = TimeSpan.FromDays(days) });

        Assert.Equal(CrawlStop.Refused, report.Stop);
    }

    /// <inheritdoc cref="CrawlAsync_BudgetLongerThanATimerCanHold_IsRefusedRatherThanThrown"/>
    [Fact]
    public async Task CrawlAsync_BudgetOfTimeSpanMaxValue_IsRefusedRatherThanThrown()
    {
        var crawler = Crawler();

        var report = await crawler.CrawlAsync(Scope() with { Budget = TimeSpan.MaxValue });

        Assert.Equal(CrawlStop.Refused, report.Stop);
    }

    /// <summary>The frontier a link walk builds is bounded by something.</summary>
    /// <remarks>
    /// <c>MaxPages</c> bounds what is read, and nothing bounds what is remembered on the way there.
    /// <c>Onward</c> adds every in-scope link of every page it read to <c>Visit.Seen</c> and to the next
    /// level's frontier, whether or not there is any room left to read them — so the cost of a crawl of a site
    /// is pages read times links per page, held as <see cref="Link"/> objects for the length of the crawl.
    /// <para>
    /// Two hundred pages of five hundred links each is a hundred thousand of them, and a real navigation
    /// carries more links than a real page has room for. The ceiling that is meant to make a crawl affordable
    /// governs neither number.
    /// </para>
    /// <para>
    /// THE COUNTER IS PROCESS-WIDE AND THE THRESHOLD IS LOOSE FOR THE SAME REASON: this crawl is
    /// asynchronous, so the work does not stay on one thread and
    /// <see cref="GC.GetAllocatedBytesForCurrentThread"/> would measure only whichever part of it happened to
    /// land back here. <see cref="GC.GetTotalAllocatedBytes"/> counts everything — including the other test
    /// classes xunit is running in parallel — so a tight budget here would fail on a busy run and say nothing
    /// about this code. Two hundred and fifty-six megabytes is a guard against holding them ALL, which is the
    /// claim in the name, rather than a budget.
    /// </para>
    /// <para>
    /// The synchronous checks in <c>Web.Tests</c> use the per-thread counter and can afford thresholds three
    /// times their measurement, because nothing else can contaminate them. Which counter to reach for is
    /// decided by whether the code under measurement stays on one thread, and getting it wrong is not a small
    /// error: the first attempt at those checks reported a refusal costing twenty-five megabytes at one depth
    /// and under one at another, for work that is identical by construction.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CrawlAsync_PagesCarryingFarMoreLinksThanItMayRead_DoesNotHoldThemAll()
    {
        var linked = string.Concat(Enumerable.Range(0, 2_000)
            .Select(index => $"<a href=\"/page-{index}\">{index}</a>"));

        var crawler = Crawler(new Linking($"<html><body>{linked}</body></html>"));

        var before = GC.GetTotalAllocatedBytes(precise: true);

        await crawler.CrawlAsync(Scope() with { MaxPages = 20, MaxDepth = 3 });

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.True(
            allocated < 256L * 1024 * 1024,
            $"a crawl allowed twenty pages allocated {allocated / (1024 * 1024)} MB walking the links of them");
    }

    private static CrawlScope Scope() => new()
    {
        Start = Start,
        MaxPages = 10,
        Budget = TimeSpan.FromSeconds(5),
        MaxDepth = 1,
        IncludesSubdomains = false,
        IsSitemapPreferred = true,
        IsNewestFirst = true
    };

    private static Crawler Crawler(IPageReader? reader = null)
        => new(
            reader ?? new Blank(),
            new NoIndex(),
            new LinkExtraction(),
            new AlwaysOpen(),
            Options.Create(new ScrapingOptions { MaxDegreeOfParallelism = 4 }),
            Options.Create(new CrawlOptions()),
            NullLogger<Crawler>.Instance);

    /// <summary>A site that publishes no list, so the crawl falls back to following links.</summary>
    private sealed class NoIndex : ISiteIndex
    {
        public Task<SiteListing> ListedAsync(CrawlScope scope, int room, CancellationToken ct = default)
            => Task.FromResult(SiteListing.None);
    }

    /// <summary>A page with nothing on it, for the checks that never reach a page at all.</summary>
    private sealed class Blank : IPageReader
    {
        public Task<PageOutcome> ReadAsync(Link address, CancellationToken ct = default)
            => ReadAsync(address, Appetite.Everything, ct);

        public Task<PageOutcome> ReadAsync(Link address, Appetite appetite, CancellationToken ct = default)
            => Task.FromResult(PageOutcome.Success(
                new PageContent(address, "<html><body>ok</body></html>", "ok", DateTimeOffset.UtcNow)));
    }

    /// <summary>Every page of the site is the same wall of links, which is what a navigation bar is.</summary>
    private sealed class Linking(string html) : IPageReader
    {
        public Task<PageOutcome> ReadAsync(Link address, CancellationToken ct = default)
            => ReadAsync(address, Appetite.Everything, ct);

        public Task<PageOutcome> ReadAsync(Link address, Appetite appetite, CancellationToken ct = default)
            => Task.FromResult(PageOutcome.Success(
                new PageContent(address, html, new string('t', 300), DateTimeOffset.UtcNow)));
    }

    /// <summary>A gate that never holds anyone back; none of these checks is about pacing.</summary>
    private sealed class AlwaysOpen : IPoliteness
    {
        public Task<IAsyncDisposable> EnterAsync(Uri url, TimeSpan? requested = null, CancellationToken ct = default)
            => Task.FromResult<IAsyncDisposable>(new Turn());

        public void Rest(Uri url, TimeSpan gap)
        {
        }

        public DateTimeOffset OpensAt(Uri url) => DateTimeOffset.MinValue;

        private sealed class Turn : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
