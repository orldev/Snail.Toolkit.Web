namespace Snail.Toolkit.Web.Text;

/// <summary>
/// Reduces a document to the text a reader would see.
/// </summary>
public interface ITextExtraction
{
    /// <summary>Strips scripts, styles and frames and returns the readable text.</summary>
    string Clean(string html);

    /// <summary>
    /// Answers the document's own name — its <c>&lt;title&gt;</c>, or its first heading when it has none — or
    /// <see langword="null"/> when it has neither.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Clean"/> because the title is not part of what a reader sees on the page and
    /// is lost with the head otherwise. A search result and an index entry both need the page's name, and the
    /// first line of body text is a poor stand-in for it.
    /// </remarks>
    string? Title(string html);
}
