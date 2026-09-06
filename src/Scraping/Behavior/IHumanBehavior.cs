using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Scraping.Behavior;

/// <summary>
/// Performs the activity a real visitor would produce on a loaded page.
/// </summary>
public interface IHumanBehavior
{
    /// <summary>Moves, scrolls and pauses on the page, giving up quietly when the budget runs out.</summary>
    /// <remarks>
    /// Never fails the read: simulation is decoration on top of a page that has already loaded, so a closed
    /// target or an exhausted budget ends the simulation rather than the request.
    /// </remarks>
    Task SimulateAsync(IPage page, CancellationToken ct = default);
}
