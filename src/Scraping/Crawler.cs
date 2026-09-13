using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Streaming;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Reads a list of addresses, or discovers and reads a whole site.
/// </summary>
/// <remarks>
/// The fan-out itself lives in the core, where search reuses it. What is decided here is how many pages may be
/// open at once, how far the walk goes, and when to stop.
/// <para>
/// Where a site's addresses come from is <see cref="ISiteIndex"/>'s, not this class's. Following links is the
/// fallback, not the plan: a site that publishes a list hands over its own pages — including the ones nothing
/// links to — and reading that list is the batch this class already does. The link walk exists for sites that
/// publish none, and it runs level by level so that the frontier, not the call stack, holds what is left to read.
/// </para>
/// </remarks>
public sealed class Crawler(
    IPageReader reader,
    ISiteIndex index,
    ILinkExtraction links,
    IOptions<ScrapingOptions> scraping,
    IOptions<CrawlOptions> crawling,
    ILogger<Crawler> logger) : ICrawler
{
    /// <summary>How much text a page needs before two identical ones are taken for one document.</summary>
    private const int DistinctiveText = 200;

    private readonly ScrapingOptions _scraping = scraping.Value;
    private readonly CrawlOptions _crawling = crawling.Value;

    private readonly ContentBudget _budget = crawling.Value.MaxTextCharacters > 0
        ? ContentBudget.Of(crawling.Value.MaxTextCharacters)
        : ContentBudget.Unlimited;

    /// <inheritdoc />
    public IAsyncEnumerable<PageOutcome> StreamAsync(IEnumerable<Link> addresses, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        return Fanout.OverAsync(
            addresses,
            async (address, token) => await reader.ReadAsync(address, token),
            _scraping.MaxDegreeOfParallelism,
            ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PageOutcome>> ReadAllAsync(
        IEnumerable<Link> addresses,
        CancellationToken ct = default)
    {
        var results = new List<PageOutcome>();

        await foreach (var outcome in StreamAsync(addresses, ct))
            results.Add(outcome);

        return results;
    }

    /// <inheritdoc />
    public CrawlScope ScopeOf(Link start)
    {
        ArgumentNullException.ThrowIfNull(start);

        return new CrawlScope
        {
            Start = start,
            MaxPages = _crawling.MaxPages,
            Budget = _crawling.Budget,
            MaxDepth = _crawling.MaxDepth,
            IncludesSubdomains = _crawling.IncludesSubdomains,
            IsSitemapPreferred = _crawling.IsSitemapPreferred,
            IsNewestFirst = _crawling.IsNewestFirst
        };
    }

    /// <inheritdoc />
    public async Task<CrawlReport> CrawlAsync(CrawlScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var started = Stopwatch.GetTimestamp();

        if (Impossible(scope) is { } reason)
            return Refused(scope, started, reason);

        var visit = new Visit(scope);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(scope.Budget);

        try
        {
            await VisitAsync(visit, budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            visit.Stop = CrawlStop.TimeLimit;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);

        logger.LogInformation(
            "Crawl of {Start} read {Read} pages and failed on {Failed} in {Elapsed}, ending as {Stop}",
            scope.Start, visit.Pages.Count(page => page.IsSuccess), visit.Pages.Count(page => !page.IsSuccess),
            elapsed, visit.Stop);

        return new CrawlReport
        {
            Start = scope.Start,
            Pages = visit.Pages,
            Stop = visit.Stop,
            IsFromSitemap = visit.IsFromSitemap,
            Elapsed = elapsed
        };
    }

    private async Task VisitAsync(Visit visit, CancellationToken ct)
    {
        var listed = await index.ListedAsync(visit.Scope, visit.Scope.MaxPages - 1, ct);

        if (!listed.IsPublished)
        {
            await FollowAsync(visit, ct);

            return;
        }

        visit.IsFromSitemap = true;
        visit.HasMore = listed.HasMore;

        await LevelAsync(visit, listed.Pages, depth: 0, isFollowing: false, ct);

        visit.Stop = visit.IsFull ? CrawlStop.PageLimit : CrawlStop.Exhausted;
    }

    /// <summary>Walks the site level by level, reading everything one link further out each time.</summary>
    /// <remarks>
    /// The fan-out reads a fixed sequence and cannot be grown while it runs, which is exactly what a frontier
    /// does. Levels are the shape that fits: read this level, collect what it points at, read that.
    /// </remarks>
    private async Task FollowAsync(Visit visit, CancellationToken ct)
    {
        IReadOnlyList<Link> frontier = [visit.Scope.Start];
        visit.Seen.Add(visit.Scope.Start);

        for (var depth = 0; depth <= visit.Scope.MaxDepth; depth++)
        {
            if (frontier.Count == 0)
            {
                visit.Stop = CrawlStop.Exhausted;

                return;
            }

            frontier = await LevelAsync(visit, frontier, depth, isFollowing: true, ct);

            if (visit.IsFull)
            {
                visit.Stop = CrawlStop.PageLimit;

                return;
            }
        }

        visit.Stop = frontier.Count == 0 ? CrawlStop.Exhausted : CrawlStop.DepthLimit;
    }

    private async Task<IReadOnlyList<Link>> LevelAsync(
        Visit visit,
        IReadOnlyList<Link> frontier,
        int depth,
        bool isFollowing,
        CancellationToken ct)
    {
        var wanted = frontier.Take(visit.Room).ToArray();

        if (wanted.Length < frontier.Count)
            visit.HasMore = true;

        List<Link> next = [];

        await foreach (var outcome in StreamAsync(wanted, ct))
        {
            if (Recorded(visit, outcome, depth) is { } page)
                visit.Pages.Add(page);

            if (!isFollowing || !outcome.IsSuccess)
                continue;

            next.AddRange(Onward(visit, outcome.Value));
        }

        return next;
    }

    private IEnumerable<Link> Onward(Visit visit, PageContent page)
    {
        if ((page.Final.Address ?? page.Requested.Address) is not { } origin)
            yield break;

        foreach (var link in links.From(page.Html, origin))
        {
            if (visit.Scope.Covers(link) && visit.Seen.Add(link))
                yield return link;
        }
    }

    /// <summary>Turns one read into what the report keeps, or into nothing when it is a page already kept.</summary>
    /// <remarks>
    /// The same document reaches a crawl under several addresses on nearly every real site, and
    /// <see cref="Link"/> only reduces the forms of one address, never two addresses to one document. Two
    /// answers are needed because sites give one or the other. A page that names a canonical address is filed
    /// under it and counts as that page. A site that names none — <c>/</c> and <c>/index.html</c> on a plain
    /// static site, measured against a real one — is caught by the text being identical.
    /// </remarks>
    private Outcome<CrawlPage, PageFailure>? Recorded(Visit visit, PageOutcome outcome, int depth)
    {
        if (!outcome.IsSuccess)
            return Outcome<CrawlPage, PageFailure>.Failed(outcome.Failure);

        var page = outcome.Value;
        var address = Claimed(visit, page);

        if (!visit.Kept.Add(address) || IsSaidAlready(visit, page.Text))
            return null;

        var budgeted = _budget.Apply(page.Text, page.OriginalLength);

        return Outcome<CrawlPage, PageFailure>.Success(
            new CrawlPage(address, depth, budgeted.Text, page.FetchedAt)
            {
                Title = page.Title,
                IsTruncated = budgeted.IsTruncated,
                OriginalLength = budgeted.OriginalLength
            });
    }

    /// <summary>Answers whether this crawl already kept a page saying exactly this.</summary>
    /// <remarks>
    /// Only text long enough to be its own evidence is compared. Two thin pages — a navigation fragment, an
    /// empty category — are identical for reasons that have nothing to do with being one document, and
    /// collapsing those would lose real pages rather than duplicates.
    /// </remarks>
    private static bool IsSaidAlready(Visit visit, string text)
    {
        if (text.Length < DistinctiveText)
            return false;

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        return !visit.Bodies.Add(digest);
    }

    /// <summary>Takes the address the page claims for itself, where it claims one this crawl may use.</summary>
    private Link Claimed(Visit visit, PageContent page)
    {
        if ((page.Final.Address ?? page.Requested.Address) is not { } origin)
            return page.Final;

        if (links.CanonicalOf(page.Html, origin) is not { } canonical || !visit.Scope.IsOnTheSite(canonical))
            return page.Final;

        visit.Seen.Add(canonical);

        return canonical;
    }

    /// <summary>What makes a scope impossible to crawl, or nothing when it is one that can be walked.</summary>
    /// <remarks>
    /// A scope is built by the caller — every ceiling on it is <c>required</c>, and <see cref="ScopeOf"/> is
    /// only the convenient way to start from the configured ones — so nothing validated at start-up covers it.
    /// The ceilings were read without being checked, and a budget of zero reached
    /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>, which answers a setting mistake with an
    /// <see cref="ArgumentOutOfRangeException"/> out of the middle of a crawl. A refusal is the shape the rest
    /// of this class already answers in.
    /// </remarks>
    private static string? Impossible(CrawlScope scope) => scope switch
    {
        { Start.IsAbsolute: false } => "Not an absolute http or https address.",
        { MaxPages: <= 0 } => "A crawl allowed to read no pages has nothing to do.",
        { MaxDepth: < 0 } => "A crawl cannot follow links to a depth below zero.",
        _ when scope.Budget <= TimeSpan.Zero => "A crawl needs a time budget above zero to read anything.",
        _ => null
    };

    private static CrawlReport Refused(CrawlScope scope, long started, string reason)
        => new()
        {
            Start = scope.Start,
            Pages =
            [
                Outcome<CrawlPage, PageFailure>.Failed(
                    new PageFailure(FailureKind.Navigation, reason) { Address = scope.Start })
            ],
            Stop = CrawlStop.Refused,
            IsFromSitemap = false,
            Elapsed = Stopwatch.GetElapsedTime(started)
        };

    /// <summary>Everything one crawl knows while it runs.</summary>
    private sealed class Visit(CrawlScope scope)
    {
        public CrawlScope Scope { get; } = scope;

        public HashSet<Link> Seen { get; } = [];

        public HashSet<Link> Kept { get; } = [];

        public HashSet<string> Bodies { get; } = [];

        public List<Outcome<CrawlPage, PageFailure>> Pages { get; } = [];

        public CrawlStop Stop { get; set; } = CrawlStop.Exhausted;

        public bool IsFromSitemap { get; set; }

        public bool HasMore { get; set; }

        public int Room => Math.Max(0, Scope.MaxPages - Pages.Count);

        public bool IsFull => Room == 0 && HasMore;
    }
}
