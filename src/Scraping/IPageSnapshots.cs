namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Takes a picture of a page with a real browser.
/// </summary>
/// <remarks>
/// The text of a page says what it says; a picture says what it shows — a chart, a map, a layout, a page
/// whose content is drawn rather than written. Added on 2026-10-04 for the MCP server built on this toolkit,
/// where the reader is a model that can look.
/// <para>
/// A picture is a visit like any other: the same identity, the same politeness gate, the same robots rules
/// and the same judgement of a wall, because it goes through the same <see cref="IAttempts{TValue}"/> and
/// <see cref="Browsers.IVisits"/> the page reader does. A site that would refuse the text refuses the picture.
/// </para>
/// </remarks>
public interface IPageSnapshots
{
    /// <summary>Pictures the screen a visitor sees on arriving at <paramref name="address"/>.</summary>
    Task<Outcome<PageSnapshot, PageFailure>> TakeAsync(Link address, CancellationToken ct = default);

    /// <summary>Pictures as much of <paramref name="address"/> as the frame takes in.</summary>
    Task<Outcome<PageSnapshot, PageFailure>> TakeAsync(Link address, SnapshotFrame frame, CancellationToken ct = default);
}
