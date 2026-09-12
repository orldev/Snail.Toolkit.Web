using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Sitemaps;
using Snail.Toolkit.Web.Streaming;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Reads a list of addresses, or discovers and reads a whole site.
/// </summary>
/// <remarks>
/// The fan-out itself lives in the core, where search reuses it. What is decided here is how many pages may be
/// open at once, where a site's addresses come from, and when to stop.
/// <para>
/// Following links is the fallback, not the plan. A site that publishes a sitemap hands over its own list of
/// pages — including the ones nothing links to — and reading that list is the batch this class already does.
/// The link walk exists for sites that publish none, and it runs level by level so that the frontier, not the
/// call stack, holds what is left to read.
/// </para>
/// </remarks>
public sealed class Crawler(
    IPageReader reader,
    ISiteRules rules,
    ISitemapSource sitemaps,
    ILinkExtraction links,
    IIdentityRotation identities,
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
            IsSitemapPreferred = _crawling.IsSitemapPreferred
        };
    }

    /// <inheritdoc />
    public async Task<CrawlReport> CrawlAsync(CrawlScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var started = Stopwatch.GetTimestamp();

        if (scope.Start.Address is not { } start)
            return Refused(scope, started);

        var visit = new Visit(scope, start);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(scope.Budget);

        try
        {
            await VisitAsync(visit, start, budget.Token);
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

    private async Task VisitAsync(Visit visit, Uri start, CancellationToken ct)
    {
        var proxy = identities.Next().Proxy;
        var listed = await ListedAsync(visit, start, proxy, ct);

        if (listed.Count > 0)
        {
            visit.IsFromSitemap = true;

            await LevelAsync(visit, listed, depth: 0, isFollowing: false, ct);

            visit.Stop = visit.IsFull ? CrawlStop.PageLimit : CrawlStop.Exhausted;

            return;
        }

        await FollowAsync(visit, ct);
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
            if (IsInScope(visit, link) && visit.Seen.Add(link))
                yield return link;
        }
    }

    /// <summary>Asks the site for its own list of pages before walking it.</summary>
    /// <remarks>
    /// The list is read even where <c>robots.txt</c> is not obeyed: permission and self-description are
    /// different questions, and a site that offers an index is worth taking up on it either way.
    /// </remarks>
    private async Task<IReadOnlyList<Link>> ListedAsync(
        Visit visit,
        Uri start,
        ProxyEndpoint? proxy,
        CancellationToken ct)
    {
        if (!visit.Scope.IsSitemapPreferred)
            return [];

        var origin = new Uri($"{start.Scheme}://{start.Authority}");
        var published = await rules.RulesAsync(start, proxy, ct);

        var declared = published.Sitemaps.Count > 0
            ? published.Sitemaps
            : [Link.Of(new Uri(origin, "/sitemap.xml").AbsoluteUri)];

        var pending = new Queue<Link>(declared);
        HashSet<Link> opened = [];
        HashSet<Link> known = [visit.Scope.Start];
        List<Link> found = [visit.Scope.Start];

        while (pending.Count > 0 && opened.Count < _crawling.MaxSitemaps)
        {
            var next = pending.Dequeue();

            if (next.Address is not { } document || !opened.Add(next))
                continue;

            var file = await sitemaps.ReadAsync(document, proxy, ct);

            foreach (var child in file.Sitemaps)
                pending.Enqueue(child);

            foreach (var page in file.Pages)
            {
                if (!IsInScope(visit, page.Address) || !known.Add(page.Address))
                    continue;

                if (found.Count == visit.Scope.MaxPages)
                {
                    visit.HasMore = true;

                    return found;
                }

                found.Add(page.Address);
            }
        }

        if (pending.Count > 0)
            visit.HasMore = true;

        if (found.Count == 1)
        {
            logger.LogInformation("{Origin} published no sitemap worth reading; following its links instead",
                origin);

            return [];
        }

        return found;
    }

    /// <summary>Answers whether an address belongs to the site being read.</summary>
    /// <remarks>
    /// A leading <c>www.</c> is dropped before the subdomain comparison, so a crawl started at
    /// <c>www.example.com</c> with subdomains on still covers <c>docs.example.com</c>. No public suffix list is
    /// consulted: the site is whatever the starting address named, never a guess at the company behind it.
    /// </remarks>
    private static bool IsInScope(Visit visit, Link link)
    {
        if (link.Address is not { } address)
            return false;

        if (string.Equals(address.Host, visit.Host, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!visit.Scope.IncludesSubdomains)
            return false;

        return string.Equals(address.Host, visit.Root, StringComparison.OrdinalIgnoreCase)
               || address.Host.EndsWith($".{visit.Root}", StringComparison.OrdinalIgnoreCase);
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

        var budgeted = _budget.Apply(page.Text);

        return Outcome<CrawlPage, PageFailure>.Success(
            new CrawlPage(address, depth, budgeted.Text, page.FetchedAt)
            {
                Title = page.Title,
                IsTruncated = page.IsTruncated || budgeted.IsTruncated,
                OriginalLength = Math.Max(page.OriginalLength, budgeted.OriginalLength)
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

        if (links.CanonicalOf(page.Html, origin) is not { } canonical || !IsInScope(visit, canonical))
            return page.Final;

        visit.Seen.Add(canonical);

        return canonical;
    }

    private static CrawlReport Refused(CrawlScope scope, long started)
        => new()
        {
            Start = scope.Start,
            Pages =
            [
                Outcome<CrawlPage, PageFailure>.Failed(
                    new PageFailure(FailureKind.Navigation, "Not an absolute http or https address.")
                    {
                        Address = scope.Start
                    })
            ],
            Stop = CrawlStop.Refused,
            IsFromSitemap = false,
            Elapsed = Stopwatch.GetElapsedTime(started)
        };

    /// <summary>Everything one crawl knows while it runs.</summary>
    private sealed class Visit(CrawlScope scope, Uri start)
    {
        public CrawlScope Scope { get; } = scope;

        public string Host { get; } = start.Host;

        public string Root { get; } = start.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? start.Host[4..]
            : start.Host;

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
