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

    /// <summary>Runs one query and answers with links.</summary>
    /// <remarks>
    /// Throws on transport failure rather than swallowing it: the caller records the attempt, and a provider
    /// that returned an empty list on a 503 would be indistinguishable from one that simply found nothing.
    /// </remarks>
    Task<IReadOnlyList<SearchResult>> FindAsync(string query, CancellationToken ct = default);
}
