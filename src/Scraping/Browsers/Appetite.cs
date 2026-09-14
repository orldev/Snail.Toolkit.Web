namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// How much of a page the browser is allowed to fetch.
/// </summary>
/// <remarks>
/// A site that counts requests rather than pages reads one ordinary page load as a burst, because that is what
/// it is. Measured against Brave on 2026-09-14 from one address, arms alternating and a fresh context each:
/// the full page cost 71 to 73 requests and was answered <c>429</c> three times out of three, while the
/// document alone cost one request and was answered <c>200</c> three times out of three — carrying all
/// eighteen result blocks, because that engine renders its results on the server.
/// <para>
/// So this is not a way around anything. Fetching the images, fonts, styles and beacons of a page that will
/// only be parsed is bandwidth nobody wanted and load the site did not have to carry; asking for the document
/// is both lighter on the site and what gets an answer. It stays a choice rather than a default because a page
/// that is driven rather than read needs its scripts: Google's query is typed into a form that does not exist
/// without them.
/// </para>
/// </remarks>
public enum Appetite
{
    /// <summary>Everything the page asks for, the way a person's browser would.</summary>
    Everything,

    /// <summary>The document alone; every subresource it asks for is refused.</summary>
    DocumentOnly
}
