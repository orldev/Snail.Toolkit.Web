namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Why a crawl ended.
/// </summary>
/// <remarks>
/// A crawl that stopped early and one that read the whole site look identical from the pages alone, and the
/// difference decides whether the answer can be trusted as complete. That is why a report states this rather
/// than leaving a caller to compare counts against ceilings.
/// </remarks>
public enum CrawlStop
{
    /// <summary>Everything in scope was read.</summary>
    Exhausted,

    /// <summary>The page ceiling was reached; the site has more.</summary>
    PageLimit,

    /// <summary>The time budget ran out; the site has more.</summary>
    TimeLimit,

    /// <summary>The depth ceiling was reached while following links; deeper pages were not read.</summary>
    DepthLimit,

    /// <summary>The starting address was not one that could be read at all.</summary>
    Refused
}
