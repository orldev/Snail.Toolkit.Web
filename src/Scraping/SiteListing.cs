namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// What a site said when it was asked for its own list of pages.
/// </summary>
/// <param name="Pages">The addresses to read, in the order they are to be read.</param>
/// <param name="HasMore">Whether the site listed more than the ceiling allowed to be taken.</param>
/// <remarks>
/// <paramref name="HasMore"/> is carried rather than inferred from the count, because a list that filled the
/// ceiling exactly and a list that overflowed it are different facts and only the second one means the crawl
/// stopped short of the site.
/// </remarks>
public sealed record SiteListing(IReadOnlyList<Link> Pages, bool HasMore)
{
    /// <summary>A site that publishes no list worth reading.</summary>
    public static SiteListing None { get; } = new([], false);

    /// <summary>Gets whether the site offered a list at all.</summary>
    public bool IsPublished => Pages.Count > 0;
}
