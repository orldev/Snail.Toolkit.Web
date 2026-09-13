using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Sitemaps;

namespace Snail.Toolkit.Web.Scraping.Sitemaps;

/// <summary>
/// Builds a site's list of pages from the sitemaps it points at.
/// </summary>
/// <remarks>
/// The list is read even where <c>robots.txt</c> is not obeyed: permission and self-description are different
/// questions, and a site that offers an index is worth taking up on it either way. Where the file names no
/// sitemap, <c>/sitemap.xml</c> is tried, because that is where one usually is.
/// </remarks>
public sealed class SitemapIndex(
    ISiteRules rules,
    ISitemapSource sitemaps,
    IIdentityRotation identities,
    IOptions<CrawlOptions> crawling,
    ILogger<SitemapIndex> logger) : ISiteIndex
{
    private readonly CrawlOptions _crawling = crawling.Value;

    /// <inheritdoc />
    public async Task<SiteListing> ListedAsync(CrawlScope scope, int room, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (!scope.IsSitemapPreferred || room <= 0 || scope.Start.Address is not { } start)
            return SiteListing.None;

        var proxy = identities.Next().Proxy;
        var origin = new Uri($"{start.Scheme}://{start.Authority}");
        var published = await rules.RulesAsync(start, proxy, ct);

        var declared = published.Sitemaps.Count > 0
            ? published.Sitemaps
            : [Link.Of(new Uri(origin, "/sitemap.xml").AbsoluteUri)];

        var found = await OpenedAsync(scope, declared, proxy, room, ct);

        if (found.Pages.Count == 0)
        {
            logger.LogInformation("{Origin} published no sitemap worth reading; following its links instead",
                origin);

            return SiteListing.None;
        }

        return found;
    }

    /// <summary>Walks the declared sitemaps, opening an index's parts until the ceiling or the budget is reached.</summary>
    /// <remarks>
    /// Reading every part of a large index to find the newest pages would be a crawl of its own, so the newest
    /// parts are opened first and the newest of what they list is taken.
    /// </remarks>
    private async Task<SiteListing> OpenedAsync(
        CrawlScope scope,
        IReadOnlyList<Link> declared,
        ProxyEndpoint? proxy,
        int room,
        CancellationToken ct)
    {
        var pending = new Queue<Link>(declared);
        HashSet<Link> opened = [];
        HashSet<Link> known = [scope.Start];
        List<SitemapEntry> found = [];

        while (pending.Count > 0 && opened.Count < _crawling.MaxSitemaps && found.Count < room)
        {
            var next = pending.Dequeue();

            if (next.Address is not { } document || !opened.Add(next))
                continue;

            var file = await sitemaps.ReadAsync(document, proxy, ct);

            foreach (var child in Ordered(scope, file.Sitemaps))
                pending.Enqueue(child.Address);

            foreach (var page in file.Pages)
            {
                if (scope.Covers(page.Address) && known.Add(page.Address))
                    found.Add(page);
            }
        }

        if (found.Count == 0)
            return SiteListing.None;

        return new SiteListing(
            [scope.Start, .. Ordered(scope, found).Take(room).Select(page => page.Address)],
            pending.Count > 0 || found.Count > room);
    }

    /// <summary>The entries in the order they are read: by the site's own dates, newest first, when that is asked for.</summary>
    /// <remarks>
    /// The sort is stable and an undated entry sorts last, so a site that dates nothing is read in its own order,
    /// and one that dates only some of its pages has those read first. Applied to the parts of an index before
    /// they are opened and to the pages they list before the ceiling is applied.
    /// </remarks>
    private static IEnumerable<SitemapEntry> Ordered(CrawlScope scope, IEnumerable<SitemapEntry> entries)
        => scope.IsNewestFirst
            ? entries.OrderByDescending(entry => entry.LastModified ?? DateTimeOffset.MinValue)
            : entries;
}
