namespace Snail.Toolkit.Web.Text;

/// <summary>
/// Raised when a page's markup nests deeper than this library is willing to parse.
/// </summary>
/// <remarks>
/// The one exception type this toolkit defines, and it is one because every alternative makes an extraction
/// lie. <c>Clean</c> answers a string, <c>Title</c> a string or nothing, <c>From</c> a list — markup that was
/// never read would come back from all three as "this page has no text, no name and no links", which is a
/// statement about the page and not about us. A read that ends in this has not looked at the page at all.
/// <para>
/// A consumer of the engines never sees it: the page-reading tail turns it into
/// <c>FailureKind.Unreadable</c>, which is where an answer of this shape belongs. It reaches only a consumer
/// calling <see cref="ITextExtraction"/> or <see cref="ILinkExtraction"/> with markup of their own.
/// </para>
/// </remarks>
/// <param name="mostNesting">The depth the parse stopped at.</param>
/// <param name="cause">What the parser said, when it said anything.</param>
public sealed class MarkupTooDeepException(int mostNesting, Exception? cause = null)
    : Exception(
        $"The page's markup nests more than {mostNesting} tags deep and was not read. Parsing is quadratic "
        + "in depth, so markup like this costs seconds of CPU for a document no reader could have meant.",
        cause)
{
    /// <summary>Gets the nesting depth at which reading stopped.</summary>
    public int MostNesting { get; } = mostNesting;
}
