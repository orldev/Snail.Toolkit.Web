using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Fetches one page with a real browser and reports what came back.
/// </summary>
public interface IPageReader
{
    /// <summary>Reads <paramref name="address"/>, retrying the failures worth retrying.</summary>
    /// <remarks>
    /// Answers with an outcome rather than a string so a caller can tell a blocked request from a timeout from
    /// a genuinely empty page. Only cancellation propagates as an exception — an address that is not an
    /// absolute http one comes back as a failure, because a list of links to read routinely holds one.
    /// </remarks>
    Task<PageOutcome> ReadAsync(Link address, CancellationToken ct);

    /// <summary>Reads <paramref name="address"/>, fetching only as much of it as the appetite allows.</summary>
    /// <remarks>
    /// An overload rather than a defaulted parameter, so that reading a page stays one short line for the
    /// callers who want the whole of it — which is nearly all of them.
    /// </remarks>
    Task<PageOutcome> ReadAsync(Link address, Appetite appetite, CancellationToken ct = default);

    /// <summary>Reads <paramref name="address"/>, taking the page only once <paramref name="readyWhen"/> matches or the wait runs out.</summary>
    /// <remarks>
    /// For an engine that draws its results with script after the document has loaded. Measured on 2026-11-07
    /// against DuckDuckGo's own site, three queries: no result was in the document at <c>DOMContentLoaded</c>,
    /// and the first appeared 340 to 870 ms after it. A read taken at the document would have parsed into
    /// nothing on a good day, and only the pause the behaviour happens to add stood between the two.
    /// <para>
    /// Internal, with a body, because it is a decision this toolkit makes for its own engines and not a knob a
    /// consumer should hold — the same reason the engines' readers are keyed. A reader that does not know how
    /// to wait, such as a test's stub, reads the page as it would anyway.
    /// </para>
    /// </remarks>
    internal Task<PageOutcome> ReadWhenReadyAsync(
        Link address,
        Appetite appetite,
        string readyWhen,
        CancellationToken ct)
        => ReadAsync(address, appetite, ct);
}
