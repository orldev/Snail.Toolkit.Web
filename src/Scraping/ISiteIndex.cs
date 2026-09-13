namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// The list of its own pages a site publishes, where it publishes one.
/// </summary>
/// <remarks>
/// Reading a whole domain is usually not a crawl at all: a site that publishes an index hands over the list of
/// its pages, including the ones nothing links to, and reading a list of addresses is a batch the reader already
/// does. Following links is the fallback for sites that publish none.
/// <para>
/// A port of its own because the crawl has two questions and only one of them is walking: where the addresses
/// come from is the other, and it is the one that changes. Conditional requests, a remembered index, a list a
/// caller supplies from somewhere else — each is another answer to this and none of them is a change to the walk.
/// </para>
/// </remarks>
public interface ISiteIndex
{
    /// <summary>Asks the site for its own list, taking at most <paramref name="room"/> addresses from it.</summary>
    Task<SiteListing> ListedAsync(CrawlScope scope, int room, CancellationToken ct = default);
}
