namespace Snail.Toolkit.Web.Scraping.Behavior;

/// <summary>
/// How much human-like activity to perform on a loaded page.
/// </summary>
/// <remarks>
/// Every setting here used to be unreachable from configuration: the registration passed a hard-coded instance
/// that also silently overrode the defaults declared on this class, and the pause range was a tuple, which the
/// configuration binder cannot fill. The range is two properties for that reason.
/// </remarks>
public sealed class HumanBehaviorOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Scraping:HumanBehavior";

    /// <summary>Gets or sets whether to simulate activity at all.</summary>
    /// <remarks>
    /// Simulation costs seconds per page. It earns them against a site that scores behaviour and wastes them
    /// against one that does not, so it is a decision the caller makes per deployment.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets how far the page may be scrolled, in CSS pixels.</summary>
    public int MaxScrollDistance { get; set; } = 1500;

    /// <summary>Gets or sets the chance of hovering an interactive element, from 0 to 1.</summary>
    public double HoverProbability { get; set; } = 0.5;

    /// <summary>Gets or sets the shortest pause between actions, in milliseconds.</summary>
    public int MinPauseMs { get; set; } = 500;

    /// <summary>Gets or sets the longest pause between actions, in milliseconds.</summary>
    public int MaxPauseMs { get; set; } = 2000;

    /// <summary>Gets or sets how many points a mouse path is drawn through.</summary>
    public int MouseSteps { get; set; } = 25;

    /// <summary>Gets or sets the budget for the whole simulation on one page.</summary>
    /// <remarks>
    /// Simulation is decoration, not the goal. Without a ceiling a page that never settles turns an optional
    /// flourish into an unbounded wait, which is what scraping can least afford.
    /// </remarks>
    public TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(15);
}
