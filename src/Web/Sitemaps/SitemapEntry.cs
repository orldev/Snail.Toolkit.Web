namespace Snail.Toolkit.Web.Sitemaps;

/// <summary>
/// One page a site lists in its sitemap.
/// </summary>
/// <param name="Address">Where the page lives.</param>
/// <param name="LastModified">When the site says it last changed, when it says at all.</param>
/// <remarks>
/// <c>changefreq</c> and <c>priority</c> are dropped on the way in. Both are hints to a search engine about
/// what to re-read and in what order, neither is binding, and both are routinely wrong — a reader that trusted
/// them would skip pages the site claimed were yearly and had edited that morning.
/// </remarks>
public sealed record SitemapEntry(Link Address, DateTimeOffset? LastModified = null);
