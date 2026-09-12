namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// One move in a walkthrough, and the consequence it is held to.
/// </summary>
public sealed record Step(StepKind Kind, Spot Where)
{
    /// <summary>The text to type, or the option text to match when picking.</summary>
    public string? Value { get; init; }

    /// <summary>What the site must do in response; a step with none is taken on trust.</summary>
    public Effect? Expected { get; init; }

    /// <summary>How many times the move is made before the step is given up on.</summary>
    /// <remarks>
    /// Two by default because a live widget was measured reacting to one run and ignoring the next under
    /// identical code. Repeating a fruitless move is cheap; reporting a walkthrough as done when the site
    /// never noticed is not.
    /// </remarks>
    public int Attempts { get; init; } = 2;

    /// <summary>The pause between keystrokes, which is also what makes typing look typed.</summary>
    public TimeSpan Rhythm { get; init; } = TimeSpan.FromMilliseconds(120);

    /// <summary>How long the element is waited for before the move is attempted at all.</summary>
    /// <remarks>
    /// Kept apart from <see cref="Attempts"/> because repeating a move is the wrong cure for an element that
    /// is not on the page yet, and because falling back on the browser's navigation budget — tens of seconds —
    /// means spending it twice to discover that an address matches nothing. A step that runs out of this
    /// budget reports that the element never appeared, which asks for a different repair than a site that was
    /// there and ignored the move.
    /// </remarks>
    public TimeSpan Ready { get; init; } = TimeSpan.FromSeconds(5);
}
