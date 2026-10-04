namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// How much of a page a picture takes in.
/// </summary>
public enum SnapshotFrame
{
    /// <summary>What a visitor sees on arriving: the screen, and nothing below it.</summary>
    Screen,

    /// <summary>The page from top to bottom, as far as <see cref="PageSnapshots.TallestPage"/> reaches.</summary>
    WholePage
}
