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
    Task<PageOutcome> ReadAsync(Link address, CancellationToken ct = default);

    /// <summary>Reads <paramref name="address"/>, fetching only as much of it as the appetite allows.</summary>
    /// <remarks>
    /// An overload rather than a defaulted parameter, so that reading a page stays one short line for the
    /// callers who want the whole of it — which is nearly all of them.
    /// </remarks>
    Task<PageOutcome> ReadAsync(Link address, Appetite appetite, CancellationToken ct = default);
}
