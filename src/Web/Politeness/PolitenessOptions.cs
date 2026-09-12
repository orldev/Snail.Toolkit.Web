namespace Snail.Toolkit.Web.Politeness;

/// <summary>
/// How hard a single site may be pressed.
/// </summary>
/// <remarks>
/// Considerable effort used to go into fingerprints and none into request rate, which is the signal a site
/// notices first and the one no amount of canvas noise hides. Three parallel workers hitting one host from one
/// address is what gets an address banned; these limits are applied per host, so a crawl spread across many
/// sites keeps its throughput while no single site sees a burst.
/// </remarks>
public sealed class PolitenessOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Politeness";

    /// <summary>Gets or sets the smallest gap between two requests to the same host.</summary>
    public TimeSpan MinDelayPerHost { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets how many requests may be in flight against one host.</summary>
    public int MaxConcurrentPerHost { get; set; } = 1;

    /// <summary>Gets or sets the hosts that are pressed more gently than the rest, each with its own smallest gap.</summary>
    /// <remarks>
    /// One gap for every host is the wrong shape for a client that talks to a search engine and a regional
    /// newspaper in the same minute: the engines measured on 2026-09-12 closed over frequency, one of them after
    /// three answers in an evening, while a site with a sitemap does not mind a page a second. A key matches the
    /// host itself and every host beneath it, so <c>example.com</c> covers <c>www.example.com</c> too, and the
    /// most specific key wins. A site's own <c>Crawl-delay</c> still applies on top — both are floors.
    /// </remarks>
    public Dictionary<string, TimeSpan> HostDelays { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
