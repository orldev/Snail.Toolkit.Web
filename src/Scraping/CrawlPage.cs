namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// One page a crawl read.
/// </summary>
/// <param name="Address">Where the page was finally read from, after any redirect.</param>
/// <param name="Depth">How many links from the start it was found — zero for the start and for a sitemap.</param>
/// <param name="Text">What the page says, under the crawl's own text budget.</param>
/// <param name="FetchedAt">When it was read.</param>
/// <remarks>
/// This is deliberately not <see cref="PageContent"/>: it carries no markup. Five hundred pages of rendered
/// HTML is hundreds of megabytes held at once to answer a question about what a site says, and the caller that
/// wants one document reads that one address again.
/// </remarks>
public sealed record CrawlPage(Link Address, int Depth, string Text, DateTimeOffset FetchedAt)
{
    /// <summary>Gets the page's own name, or <see langword="null"/> when it has none.</summary>
    /// <remarks>
    /// The one thing kept from the head: an index built over a crawl needs to name what it found, and the
    /// first line of the text is not the name of the page.
    /// </remarks>
    public string? Title { get; init; }

    /// <summary>Gets whether the text was cut to fit a budget, the crawl's or the reader's.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>Gets how long the text was before any budget was applied.</summary>
    public int OriginalLength { get; init; } = Text.Length;
}
