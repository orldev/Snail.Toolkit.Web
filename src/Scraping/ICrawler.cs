namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Reads many pages: a list of addresses already known, or a whole site that is not.
/// </summary>
/// <remarks>
/// The two belong together because the second is built on the first. A site that publishes a sitemap is not
/// crawled at all — its own list of pages is read, which is faster, more complete and far less traffic than
/// following links, and only a site that publishes none is walked link by link.
/// </remarks>
public interface ICrawler
{
    /// <summary>Streams each outcome the moment it is ready, in completion order.</summary>
    /// <remarks>
    /// A failed address is a result, not an exception: one unreachable site must not cancel the batch. Every
    /// outcome carries its own address — the page on success, the failure on everything else — so nothing has
    /// to be matched back to the input by position.
    /// </remarks>
    IAsyncEnumerable<PageOutcome> StreamAsync(IEnumerable<Link> addresses, CancellationToken ct = default);

    /// <summary>Reads every address and answers once they are all done.</summary>
    Task<IReadOnlyList<PageOutcome>> ReadAllAsync(IEnumerable<Link> addresses, CancellationToken ct = default);

    /// <summary>Builds the scope a crawl gets when the caller states nothing but where to start.</summary>
    /// <remarks>Narrow it with a <c>with</c> expression rather than restating the configured ceilings.</remarks>
    CrawlScope ScopeOf(Link start);

    /// <summary>Reads a whole site and reports what it found and why it stopped.</summary>
    /// <remarks>
    /// Minutes and tens of pages, against seconds for a single address. Cancellation propagates; running out of
    /// the scope's own time budget does not, because everything read so far is the answer.
    /// </remarks>
    Task<CrawlReport> CrawlAsync(CrawlScope scope, CancellationToken ct = default);
}
