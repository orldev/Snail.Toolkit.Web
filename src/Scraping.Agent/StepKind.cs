namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// The kinds of move a walkthrough is made of.
/// </summary>
/// <remarks>
/// Deliberately three. Every widget met so far reduces to typing into a field, choosing from what the field
/// offered, and pressing the thing that submits. Waiting is not among them because a wait is not a move: it is
/// the <see cref="Effect"/> a move is expected to produce.
/// </remarks>
public enum StepKind
{
    /// <summary>Type into a field one key at a time.</summary>
    Type,

    /// <summary>Choose the offered option whose text matches.</summary>
    Pick,

    /// <summary>Press an element.</summary>
    Click
}
