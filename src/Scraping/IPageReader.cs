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
}
