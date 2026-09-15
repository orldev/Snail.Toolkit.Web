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

    /// <summary>The host this provider's queries go to, or nothing when it does not talk to one.</summary>
    /// <remarks>
    /// Nothing by default because not every provider is a site: an index in the same process answers with no
    /// request at all, and giving it a host would put it behind a gate built for strangers.
    /// <para>
    /// A provider that names its host can be asked what it costs before it is asked for results. The price is
    /// not a number this interface invents — it is the per-host gate, which already holds the gap this
    /// deployment decided for that host and the moment the host next opens, and until 2026-09-15 the search
    /// could not reach it because a provider was an opaque name. The engines differ by three orders of
    /// magnitude here: measured on 2026-09-13, one of them answers at one request a second and another only
    /// at one every two minutes, and a caller with a deadline cannot spend the second on the first.
    /// </para>
    /// </remarks>
    string? Host => null;

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
    /// <para>
    /// The return type is written out here rather than behind the <c>SearchAnswer</c> alias this package uses
    /// internally, because an alias is a fact about one compilation and does not travel in a NuGet package. A
    /// consumer writing their own provider is welcome to the same short name by declaring it themselves:
    /// <code>
    /// global using SearchAnswer = Snail.Toolkit.Web.Outcome&lt;
    ///     System.Collections.Generic.IReadOnlyList&lt;Snail.Toolkit.Web.Search.SearchResult&gt;,
    ///     Snail.Toolkit.Web.Search.SearchFailure&gt;;
    /// </code>
    /// It is not shipped in the package on purpose. A global using arriving with a dependency lands in every
    /// file of the consumer's project whether they wanted it or not, and collides outright with the one they
    /// may already have declared — which is exactly what the first consumer to implement this interface did.
    /// </para>
    /// </remarks>
    Task<Outcome<IReadOnlyList<SearchResult>, SearchFailure>> FindAsync(
        string query,
        CancellationToken ct = default);
}
