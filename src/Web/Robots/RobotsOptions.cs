namespace Snail.Toolkit.Web.Robots;

/// <summary>
/// Whether a site's published rules are obeyed, and how they are kept.
/// </summary>
public sealed class RobotsOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Robots";

    /// <summary>Gets or sets whether <c>robots.txt</c> is fetched and obeyed.</summary>
    /// <remarks>
    /// Off by default only because switching it on silently would drop URLs that existing callers expect to be
    /// fetched, and a dropped URL looks exactly like a bug. Switching it on is the correct choice for any
    /// crawl of sites you do not own.
    /// </remarks>
    public bool IsObeyed { get; set; }

    /// <summary>Gets or sets how long a fetched <c>robots.txt</c> stays authoritative.</summary>
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Gets or sets the product token matched against <c>User-agent</c> groups.</summary>
    /// <remarks>
    /// A browser user agent is not a robots product token, and matching one against these groups would find
    /// nothing, so the wildcard group is the honest default.
    /// </remarks>
    public string UserAgent { get; set; } = "*";

    /// <summary>Gets or sets how long a <c>robots.txt</c> fetch may take before it is abandoned.</summary>
    /// <remarks>Applied by whichever transport implements <see cref="IRobotsSource"/>, since only it does I/O.</remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets the longest <c>Crawl-delay</c> that is actually waited between two requests.</summary>
    /// <remarks>
    /// A site's own pace is obeyed, and it is obeyed up to a point, because the figure is written by the site
    /// being read and nothing else checked it. A <c>Crawl-delay</c> of 86400 used to be waited in full: one
    /// worker standing in the gate for a day, holding both the host's only slot and one of
    /// <c>Web:Scraping:MaxDegreeOfParallelism</c>. Four lines like it in four different files stopped a whole
    /// crawl, and nothing in the log said so — a worker that is waiting is not failing.
    /// <para>
    /// A ceiling rather than the refusal that answers <c>Retry-After</c> past
    /// <see cref="Politeness.PolitenessOptions.LongestRest"/>, and the difference between the two is the
    /// difference between the headers. <c>Retry-After</c> accompanies a refusal, so handing it straight back
    /// to the caller passes on something the site actually said; <c>Crawl-delay</c> is a pace and refuses
    /// nothing, so answering it with a refusal would drop the site from the crawl rather than read it slowly.
    /// One minute is far above what any site pacing itself sensibly asks for and far below what stalls a run.
    /// </para>
    /// </remarks>
    public TimeSpan LongestCrawlDelay { get; set; } = TimeSpan.FromMinutes(1);
}
