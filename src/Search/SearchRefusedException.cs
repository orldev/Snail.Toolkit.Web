namespace Snail.Toolkit.Web.Search;

/// <summary>
/// Thrown by a provider that was turned away rather than answered.
/// </summary>
/// <remarks>
/// A provider is contracted to throw on anything that is not an answer, and a plain exception says only that
/// something went wrong. This one says what: the engine met the request with a challenge, a consent wall or a
/// rate limit, and will meet the next one the same way if it comes at once. The search records it as
/// <see cref="SearchFailureKind.Refused"/> rather than <see cref="SearchFailureKind.Faulted"/> and rests the
/// provider — a reaction that would be wrong for a fault, where the defect is on this side and resting the
/// engine only hides it. It names the provider because a caller running several needs to know which wall it
/// hit, and it lives in this package rather than beside the browser engines so the search can recognise it
/// without depending on a browser.
/// </remarks>
public class SearchRefusedException(string provider, string reason)
    : Exception($"{provider} refused the query. {reason}")
{
    /// <summary>Gets the provider that refused.</summary>
    public string Provider { get; } = provider;

    /// <summary>Gets what the provider answered with instead of results.</summary>
    public string Reason { get; } = reason;
}
