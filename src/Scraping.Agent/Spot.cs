namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// Where on a page a move lands.
/// </summary>
/// <remarks>
/// A selector is not always the honest way to name a field. Measured on a live court search on 2026-09-12: the
/// placeholder that identified its search field is written in by the site's own script, so it was present on
/// one load and absent on the next, and three walkthroughs in a row addressed a field that was not there. The
/// label beside that field is in the markup every time.
/// <para>
/// There is no implicit conversion from a string, on purpose. Which of the two ways a step names its element
/// is exactly the fact that was wrong in those three runs, so it is stated at every call site rather than
/// inferred from the shape of an argument.
/// </para>
/// </remarks>
public sealed record Spot(SpotKind Kind, string Value)
{
    /// <summary>The element a CSS selector matches.</summary>
    public static Spot Css(string selector) => new(SpotKind.Css, selector);

    /// <summary>The field a visible label names.</summary>
    public static Spot Labelled(string label) => new(SpotKind.Label, label);

    /// <inheritdoc />
    public override string ToString()
        => Kind is SpotKind.Label ? $"the field labelled '{Value}'" : $"'{Value}'";
}
