namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// The ways a step can name the element it acts on.
/// </summary>
public enum SpotKind
{
    /// <summary>A CSS selector, matched as written.</summary>
    Css,

    /// <summary>The visible text of the label that names a field.</summary>
    Label
}
