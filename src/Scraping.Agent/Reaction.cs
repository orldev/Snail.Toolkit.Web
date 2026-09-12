namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// How a page answered a move.
/// </summary>
/// <remarks>
/// Three answers and not two, because "the element was never there" and "the site ignored the move" ask for
/// opposite fixes: the first is a wrong address or a page that renders differently between loads, the second
/// is a widget that wants a real event. An earlier version collapsed both into one fruitless attempt, and the
/// message it produced sent the reader looking for the wrong one.
/// </remarks>
internal enum Reaction
{
    /// <summary>The site reacted, and the step counts as done.</summary>
    Taken,

    /// <summary>The element never appeared within the step's own budget.</summary>
    Missing,

    /// <summary>The element was there, the move was made, and nothing followed.</summary>
    Ignored
}
