namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// A picture of a page as the browser drew it.
/// </summary>
/// <param name="Requested">The address that was asked for.</param>
/// <param name="Image">The picture, encoded as <see cref="MediaType"/>.</param>
/// <param name="TakenAt">When it was taken.</param>
/// <remarks>
/// Deliberately not a part of <see cref="PageContent"/>. A reader that wants the text of five hundred pages
/// must not be handed five hundred pictures with it, and a caller that wants to see one page has no use for
/// its markup; the two are different questions with answers of very different weight.
/// </remarks>
public sealed record PageSnapshot(Link Requested, ReadOnlyMemory<byte> Image, DateTimeOffset TakenAt)
{
    /// <summary>The encoding of every picture this library takes.</summary>
    /// <remarks>
    /// JPEG rather than PNG because the picture is taken to be looked at, usually by a model, and a page of
    /// text and photographs is three to ten times smaller this way with nothing a reader would miss.
    /// </remarks>
    public const string MediaType = "image/jpeg";

    /// <summary>Gets the address the page was finally drawn at, after any redirect.</summary>
    public Link Final { get; init; } = Requested;

    /// <summary>Gets the page's own name, or <see langword="null"/> when it has none.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the HTTP status the site answered with.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Gets the width of what was pictured, in the page's own pixels.</summary>
    public int Width { get; init; }

    /// <summary>Gets the height of what was pictured, in the page's own pixels.</summary>
    public int Height { get; init; }

    /// <summary>Gets whether the page ran on below the picture.</summary>
    /// <remarks>
    /// True for a screen with more page under it, and for a whole page taller than the ceiling. A picture
    /// that stops without saying so reads as a page that ended.
    /// </remarks>
    public bool IsCut { get; init; }
}
