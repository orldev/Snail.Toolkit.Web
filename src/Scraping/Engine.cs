namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Which browser engine pages are read with.
/// </summary>
/// <remarks>
/// A deliberate choice rather than a detail, because not everything the toolkit presents survives a change of
/// engine. Client hints belong to Chromium alone: Firefox never sends <c>Sec-Ch-Ua</c>, so a browser claiming
/// Chrome without them contradicts itself, and the alignment itself goes over CDP, which no other engine
/// answers. The launch switches are Chromium's own as well.
/// <para>
/// It sits at the root of the package rather than beside the pool, and that is not tidiness. Living in
/// <c>Browsers</c> it was read by <c>Stealth</c>, which <c>Browsers</c> reads in turn, and the two namespaces
/// depended on each other in a cycle — invisible to the compiler, which only ever sees one assembly, and found
/// by asking the solution rather than by reading it. The engine is not a property of the pool anyway: the
/// setting that names it is here, and everything that has to agree with it reads it from here.
/// </para>
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
