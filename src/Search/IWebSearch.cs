namespace Snail.Toolkit.Web.Search;

/// <summary>
/// Asks every registered provider and merges what they answer.
/// </summary>
public interface IWebSearch
{
    /// <summary>Runs every query against every provider and collects what came back.</summary>
    Task<SearchOutcome> FindAsync(IEnumerable<string> queries, CancellationToken ct = default);

    /// <summary>Yields each provider-and-query attempt as it finishes, failures included.</summary>
    /// <remarks>
    /// Results are not deduplicated here — deduplication needs the whole answer, and a caller streaming
    /// attempts has chosen to see them arrive instead.
    /// </remarks>
    IAsyncEnumerable<SearchAttempt> StreamAsync(IEnumerable<string> queries, CancellationToken ct = default);
}
