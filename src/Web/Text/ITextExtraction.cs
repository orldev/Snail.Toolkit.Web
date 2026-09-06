namespace Snail.Toolkit.Web.Text;

/// <summary>
/// Reduces a document to the text a reader would see.
/// </summary>
public interface ITextExtraction
{
    /// <summary>Strips scripts, styles and frames and returns the readable text.</summary>
    string Clean(string html);
}
