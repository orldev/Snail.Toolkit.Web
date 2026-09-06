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
}
