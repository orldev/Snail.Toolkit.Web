namespace Snail.Toolkit.Web.Search;

/// <summary>
/// The links a search found, and what every provider did to find them.
/// </summary>
/// <param name="Results">The links, deduplicated across providers.</param>
/// <param name="Attempts">One entry per provider and query, failures included.</param>
/// <remarks>
/// An empty answer explains itself here rather than leaving the caller to guess: <see cref="IsAnswered"/>
/// separates "every provider answered and nothing matched" from "no provider answered at all", which read
/// identically as an empty list and call for opposite reactions.
/// </remarks>
public sealed record SearchOutcome(
    IReadOnlyList<SearchResult> Results,
    IReadOnlyList<SearchAttempt> Attempts)
{
    /// <summary>Gets whether anything was found.</summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>Gets whether at least one provider answered, with or without results.</summary>
    public bool IsAnswered => Attempts.Any(attempt => attempt.IsAnswered);
}
