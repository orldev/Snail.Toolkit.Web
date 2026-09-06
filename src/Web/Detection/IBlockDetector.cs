namespace Snail.Toolkit.Web.Detection;

/// <summary>
/// Recognises challenge, consent and refusal pages returned in place of the requested one.
/// </summary>
public interface IBlockDetector
{
    /// <summary>Judges a document and the URL the fetch ended on.</summary>
    BlockVerdict Inspect(string html, string finalUrl);
}
