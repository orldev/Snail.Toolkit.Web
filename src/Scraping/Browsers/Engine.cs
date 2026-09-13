namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Which browser engine pages are read with.
/// </summary>
/// <remarks>
/// A deliberate choice rather than a detail, because not everything the toolkit presents survives a change of
/// engine. Client hints belong to Chromium alone: Firefox never sends <c>Sec-Ch-Ua</c>, so a browser claiming
/// Chrome without them contradicts itself, and the alignment itself goes over CDP, which no other engine
/// answers. The launch switches are Chromium's own as well.
/// </remarks>
public enum Engine
{
    /// <summary>Chromium, the only engine that answers CDP.</summary>
    Chromium,

    /// <summary>Firefox, driven over the juggler protocol.</summary>
    Firefox,

    /// <summary>WebKit, driven over its own protocol.</summary>
    WebKit
}
