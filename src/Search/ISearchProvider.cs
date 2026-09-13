namespace Snail.Toolkit.Web.Search;

/// <summary>
/// A search back end that can be asked one query.
/// </summary>
/// <remarks>
/// Says nothing about transport. The contract used to carry an HTTP client name, which forced every provider
/// through <c>IHttpClientFactory</c> and left a provider driving a browser holding a client it never opens,
/// wrapped in proxy, rotation and throttling handlers that never run. A provider that needs a browser lives in
/// the companion package and implements this same interface.
/// </remarks>
public interface ISearchProvider
{
    /// <summary>Identifies the provider in configuration, metrics and logs.</summary>
    /// <remarks>
    /// Must equal the name the provider is registered under. Declare it as a <c>const</c> on the implementation
    /// and return that constant here, so registration and runtime cannot drift.
    /// </remarks>
    string Name { get; }

    /// <summary>Runs one query and answers with links, or with the reason there are none.</summary>
    /// <remarks>
    /// Failure is data here as it is everywhere else in this toolkit. A provider used to be contracted to throw,
    /// and three exception types existed to carry what an outcome carries: the search then had to tell a refusal
    /// from a fault by catching the right base class, and the two engines that fetch over HTTP never threw the
    /// refusal at all — they called <c>EnsureSuccessStatusCode</c>, so a 429 arrived as a fault and the engine
    /// asking to be left alone was never left alone.
    /// <para>
    /// An empty list is success: the provider answered and the answer held nothing, which is a fact about the
    /// query rather than about the engine. Cancellation is still an exception, because that is what the TPL and
    /// ASP.NET Core expect of it.
    /// </para>
    /// </remarks>
    Task<Outcome<IReadOnlyList<SearchResult>, SearchFailure>> FindAsync(
        string query,
        CancellationToken ct = default);
}
