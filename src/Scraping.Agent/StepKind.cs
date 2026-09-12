namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// The kinds of move a walkthrough is made of.
/// </summary>
/// <remarks>
/// Kept small on purpose, and grown only against a measurement. It began as three — type, pick, click — on the
/// claim that every widget reduces to typing into a field, choosing what the field offered, and pressing the
/// thing that submits. Measured against five search engines on 2026-09-12, the last third of that claim is
/// false: on two of them the submitting element does not exist, and on a third a click on it sends nothing.
/// Those forms are submitted by a key, which is why <see cref="Press"/> is here.
/// <para>
/// Waiting is still not among them, because a wait is not a move: it is the <see cref="Effect"/> a move is
/// expected to produce. Ticking a box is not among them either — a checkbox and a radio are pressed, so
/// <see cref="Click"/> already says it — and neither is clearing a field, which has not yet been needed by any
/// form this package has been pointed at.
/// </para>
/// </remarks>
public enum StepKind
{
    /// <summary>Type into a field one key at a time.</summary>
    Type,

    /// <summary>Choose the offered option whose text matches.</summary>
    /// <remarks>
    /// For the lists a site draws itself — a suggestion dropdown is markup like any other, and it is pressed.
    /// A real <c>select</c> element is not: that one is <see cref="Choose"/>.
    /// </remarks>
    Pick,

    /// <summary>Press an element.</summary>
    Click,

    /// <summary>Press a key while the element has the focus.</summary>
    /// <remarks>
    /// The key travels in <see cref="Step.Value"/> and defaults to Enter, which is the one that matters: a
    /// search form whose only submit is the return key cannot be walked through at all without this.
    /// </remarks>
    Press,

    /// <summary>Choose an option of a real <c>select</c> element by the text shown for it.</summary>
    /// <remarks>
    /// Separate from <see cref="Pick"/> because a native list is not markup that can be pressed: the browser
    /// draws it outside the page, so clicking an <c>option</c> is unreliable where it works at all. The option
    /// is named by its visible text rather than by its value, because the value is an internal code and the
    /// text is what a person reading the form would choose. The list need not be on screen: a site that draws
    /// its own dropdown hides the native one behind it, and the hidden one is still what the form submits.
    /// </remarks>
    Choose
}
