namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// One move in a walkthrough, and the consequence it is held to.
/// </summary>
public sealed record Step(StepKind Kind, string Selector)
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
}
