namespace Snail.Toolkit.Web.Search;

/// <summary>
/// Asks every registered provider and merges what they answer.
/// </summary>
public interface IWebSearch
{
    /// <summary>Runs every query against every provider and collects what came back.</summary>
    Task<SearchOutcome> FindAsync(IEnumerable<string> queries, CancellationToken ct);

    /// <summary>Runs every query the budget allows, and says which providers it did not stretch to.</summary>
    /// <remarks>
    /// An overload rather than a defaulted parameter, so a search stays one short line for the callers who are
    /// in no hurry — which is nearly all of them, and the shape every caller had before 2026-09-15.
    /// <para>
    /// WHAT A CALLER IS SAYING HERE IS WHAT THE QUERY IS WORTH, not which engine to use. The engines differ by
    /// three orders of magnitude — measured on 2026-09-13, one answers at a request a second and another only
    /// at one every two minutes — so a caller with a deadline had no way to ask the cheap ones only. NAMING
    /// THE ENGINE WAS THE OBVIOUS ANSWER AND IS NOT THIS ONE: the aggregator exists because which engine
    /// answers a given client from a given address is a fact that changes, and a caller that names engines has
    /// taken that fact back. A budget says the same thing in the caller's own terms and leaves the choice
    /// where the measurements are. A caller who genuinely wants one engine registers one, or resolves
    /// <see cref="ISearchProvider"/> and asks it directly; neither costs anything.
    /// </para>
    /// <para>
    /// The budget is spent on waiting, not on cutting: a provider whose host does not open inside it is not
    /// asked at all and says so as <see cref="SearchFailureKind.OutOfTime"/>, which is knowable before the
    /// request because the per-host gate holds it. What is already in flight when the budget runs out is
    /// cancelled, and comes back under the same kind rather than as
    /// <see cref="SearchFailureKind.Canceled"/> — the caller did not change its mind, the search kept the
    /// caller's promise.
    /// </para>
    /// </remarks>
    Task<SearchOutcome> FindAsync(IEnumerable<string> queries, TimeSpan budget, CancellationToken ct = default);

    /// <summary>Yields each provider-and-query attempt as it finishes, failures included.</summary>
    /// <remarks>
    /// Results are not deduplicated here — deduplication needs the whole answer, and a caller streaming
    /// attempts has chosen to see them arrive instead.
    /// </remarks>
    IAsyncEnumerable<SearchAttempt> StreamAsync(IEnumerable<string> queries, CancellationToken ct);

    /// <summary>Yields each attempt as it finishes, and each one the budget did not stretch to.</summary>
    IAsyncEnumerable<SearchAttempt> StreamAsync(
        IEnumerable<string> queries,
        TimeSpan budget,
        CancellationToken ct = default);
}
