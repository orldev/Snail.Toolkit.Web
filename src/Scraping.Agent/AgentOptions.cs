namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// Settings for walking a page through an errand.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>The configuration section these settings bind to.</summary>
    public const string SectionName = "Web:Scraping:Agent";

    /// <summary>How long the page is left alone after the last move before it is read.</summary>
    /// <remarks>
    /// A submit usually replaces the results in place rather than navigating, so there is no load state to wait
    /// for and nothing that says the answer has arrived. Everything else in an errand is held to a measured
    /// consequence; this is the one pause that cannot be, which is why it is a setting rather than a constant.
    /// </remarks>
    public TimeSpan Settle { get; set; } = TimeSpan.FromSeconds(3);
}
