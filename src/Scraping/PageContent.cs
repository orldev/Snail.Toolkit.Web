using HtmlAgilityPack;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// A page that was fetched and rendered.
/// </summary>
/// <param name="Requested">The address the caller asked for.</param>
/// <param name="Html">The document as the browser rendered it, whole.</param>
/// <param name="Text">What a reader would see on that document, after any budget was applied.</param>
/// <param name="FetchedAt">When the document was read.</param>
/// <remarks>
/// Markup and text are both carried because the two callers want opposite things and neither can recover the
/// other for free: an agent wants the text and drowns in the markup, while a search engine reads its links out
/// of the markup and finds none in the text. Only the text is budgeted. A ceiling applied to markup lands in
/// the middle of a tag, which is no longer a document any parser can follow, and everything past the cut —
/// links included — disappears without a word.
/// <para>
/// <see cref="Final"/> is kept apart from <paramref name="Requested"/> because a redirect to a consent wall or
/// a <c>/sorry/</c> page is the most common way a read silently stops being about the page that was asked for.
/// Both are <see cref="Link"/> rather than strings, so <see cref="IsRedirected"/> compares canonical forms: an
/// http address answered over https is the same page, and comparing the raw strings called that a redirect.
/// </para>
/// </remarks>
public sealed record PageContent(Link Requested, string Html, string Text, DateTimeOffset FetchedAt)
{
    /// <summary>Gets the address the browser ended on after redirects.</summary>
    public Link Final { get; init; } = Requested;

    /// <summary>Gets the document's own name, or <see langword="null"/> when it has none.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the HTTP status of the main document, when the browser reported one.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Gets the user agent the page was fetched with.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Gets whether the text was cut to fit the configured budget.</summary>
    /// <remarks>
    /// Carried beside the text as well as announced inside it, so a caller can act on the fact — widen the
    /// budget, warn the reader — without parsing prose.
    /// </remarks>
    public bool IsTruncated { get; init; }

    /// <summary>Gets how long the text was before any budget was applied.</summary>
    public int OriginalLength { get; init; } = Text.Length;

    /// <summary>Gets whether the browser was redirected away from the requested address.</summary>
    public bool IsRedirected => Requested != Final;

    /// <summary>Gets the document the read parsed, handed to the reader inside this library that reads it next.</summary>
    /// <remarks>Set only on the roads that ask for it; see <see cref="Reading"/>.</remarks>
    internal HtmlDocument? Parsed { get; init; }
}
