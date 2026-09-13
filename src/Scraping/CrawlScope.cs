namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// What one crawl is allowed to do.
/// </summary>
/// <remarks>
/// Every ceiling is stated rather than defaulted here, so that the configured defaults live in exactly one
/// place. Ask <see cref="ICrawler.ScopeOf"/> for a scope and narrow it: <c>scope with { MaxPages = 50 }</c>.
/// </remarks>
public sealed record CrawlScope
{
    /// <summary>Gets the address the crawl starts from, which also decides the site.</summary>
    public required Link Start { get; init; }

    /// <summary>Gets how many pages may be read.</summary>
    public required int MaxPages { get; init; }

    /// <summary>Gets how long the crawl may run.</summary>
    public required TimeSpan Budget { get; init; }

    /// <summary>Gets how many links deep the crawl follows where it has to follow links.</summary>
    public required int MaxDepth { get; init; }

    /// <summary>Gets whether subdomains of the starting host count as the same site.</summary>
    public required bool IncludesSubdomains { get; init; }

    /// <summary>Gets whether a published sitemap is used instead of following links.</summary>
    public required bool IsSitemapPreferred { get; init; }

    /// <summary>Gets whether the pages a sitemap lists are read newest first, by the dates the site gives them.</summary>
    public required bool IsNewestFirst { get; init; }

    /// <summary>Gets the path prefixes that are not read at all — a sponsored section, a catalogue of codes.</summary>
    /// <remarks>
    /// Compared against the path as the site wrote it, from its first character and without regard to case,
    /// whether the address came from the sitemap or from a link. Not defaulted from the configuration, because
    /// which section of a site is noise is a fact about that site alone; the start itself is never excluded.
    /// </remarks>
    public IReadOnlyList<string> Excluded { get; init; } = [];

    /// <summary>Answers whether an address is one this crawl reads: on the site, and not in an excluded section.</summary>
    /// <remarks>
    /// Asked of an address from the site's own list and of one found in a link alike, so a section left out is
    /// left out however the crawl arrived at it. The predicates live on the scope rather than in the crawl
    /// because they are what the scope means, and because the crawl is not the only thing that asks.
    /// </remarks>
    public bool Covers(Link link) => IsOnTheSite(link) && !IsExcluded(link);

    /// <summary>Answers whether an address belongs to the site being read.</summary>
    /// <remarks>
    /// A leading <c>www.</c> is dropped before the subdomain comparison, so a crawl started at
    /// <c>www.example.com</c> with subdomains on still covers <c>docs.example.com</c>. No public suffix list is
    /// consulted: the site is whatever the starting address named, never a guess at the company behind it.
    /// </remarks>
    public bool IsOnTheSite(Link link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (link.Address is not { } address || Start.Address is not { } start)
            return false;

        if (string.Equals(address.Host, start.Host, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IncludesSubdomains)
            return false;

        var root = Bare(start.Host);

        return string.Equals(address.Host, root, StringComparison.OrdinalIgnoreCase)
               || address.Host.EndsWith($".{root}", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsExcluded(Link link)
        => link.Address is { } address
           && Excluded.Any(prefix => address.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string Bare(string host)
        => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
}
