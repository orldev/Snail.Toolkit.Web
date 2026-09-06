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
}
