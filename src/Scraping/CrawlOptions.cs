namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// The ceilings a whole-domain read runs under, and what it hands back.
/// </summary>
/// <remarks>
/// Reading one page and reading a domain are separate operations because they cost different amounts and are
/// worth different things: an answer to a specific question is buried by completeness, and a crawl is partly
/// successful by nature. These are the defaults every crawl starts from; a caller narrows them per crawl.
/// <para>
/// The knob that decides how fast a crawl runs is not here. A crawl is one host, and the gate that paces
/// requests is per host, so <c>Web:Politeness:MaxConcurrentPerHost</c> governs it — raising
/// <c>Web:Scraping:MaxDegreeOfParallelism</c> only adds workers that queue at the same gate.
/// </para>
/// </remarks>
public sealed class CrawlOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Scraping:Crawl";

    /// <summary>Gets or sets how many pages a single crawl may read.</summary>
    public int MaxPages { get; set; } = 500;

    /// <summary>Gets or sets how long a single crawl may run.</summary>
    /// <remarks>
    /// A crawl that hits this keeps everything it read and says the time ran out, because a partial answer to
    /// "what is on this site" is worth having and an exception is not.
    /// </remarks>
    public TimeSpan Budget { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets or sets how many links deep the crawl follows when it has to follow links at all.</summary>
    /// <remarks>Ignored on a site with a sitemap: there is no depth in a list.</remarks>
    public int MaxDepth { get; set; } = 3;

    /// <summary>Gets or sets whether subdomains of the starting host count as the same site.</summary>
    /// <remarks>
    /// Off, because "everything about this site" normally means one host, and one careless flag turns a read of
    /// a documentation site into a read of the whole company. On, it is the shape a security audit wants.
    /// </remarks>
    public bool IncludesSubdomains { get; set; }

    /// <summary>Gets or sets whether a published sitemap is used instead of following links.</summary>
    public bool IsSitemapPreferred { get; set; } = true;

    /// <summary>Gets or sets how many characters of each page's text the crawl keeps.</summary>
    /// <remarks>
    /// Deliberately far below what one page may have. This ceiling multiplies by the number of pages, so a
    /// budget that is generous for a single fetch fills an agent's whole context on the twentieth page. Full
    /// markup is never kept at all — a crawl answers what a site says, and one address is read again with
    /// <c>ReadAsync</c> when the document itself is wanted.
    /// </remarks>
    public int MaxTextCharacters { get; set; } = 20_000;

    /// <summary>Gets or sets how many sitemap documents one crawl may open.</summary>
    /// <remarks>A large site publishes an index of hundreds of parts, and reading all of them is its own crawl.</remarks>
    public int MaxSitemaps { get; set; } = 50;

    /// <summary>Gets or sets how long one sitemap document may take to download.</summary>
    public TimeSpan SitemapTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the largest sitemap document that is read.</summary>
    public int MaxSitemapBytes { get; set; } = 32 * 1024 * 1024;
}
