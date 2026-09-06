namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// What one crawl found, and how it ended.
/// </summary>
/// <remarks>
/// A crawl is partly successful by nature — some pages answer, some are blocked, some time out — so the
/// outcome is a report rather than a list of pages or a single failure. <see cref="Stop"/> is the part a
/// caller cannot work out for itself: pages alone cannot say whether the site was read or merely sampled.
/// </remarks>
public sealed record CrawlReport
{
    /// <summary>Gets the address the crawl started from.</summary>
    public required Link Start { get; init; }

    /// <summary>Gets every address the crawl attempted, in the order they finished.</summary>
    public required IReadOnlyList<Outcome<CrawlPage, PageFailure>> Pages { get; init; }

    /// <summary>Gets why the crawl ended.</summary>
    public required CrawlStop Stop { get; init; }

    /// <summary>Gets whether the addresses came from the site's own sitemap rather than from its links.</summary>
    /// <remarks>
    /// Worth reporting because it changes what the answer covers: a sitemap lists pages no navigation links to,
    /// while following links can only reach what is linked.
    /// </remarks>
    public required bool IsFromSitemap { get; init; }

    /// <summary>Gets how long the crawl took.</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>Gets how many pages were read.</summary>
    public int Read => Pages.Count(page => page.IsSuccess);

    /// <summary>Gets how many addresses answered with a failure.</summary>
    public int Failed => Pages.Count - Read;
}
