namespace Snail.Toolkit.Web.Search;

/// <summary>
/// Why one attempt carried no results.
/// </summary>
public enum SearchFailureKind
{
    /// <summary>The provider answered and the answer held nothing.</summary>
    /// <remarks>
    /// This is an answer, not a failure of the provider — which is why
    /// <see cref="SearchOutcome.IsAnswered"/> counts it as one.
    /// </remarks>
    NoResults,

    /// <summary>The provider threw.</summary>
    Faulted,

    /// <summary>The caller cancelled before the provider answered.</summary>
    Canceled,

    /// <summary>The provider was turned away: a challenge, a consent wall, a rate limit.</summary>
    /// <remarks>
    /// Kept apart from <see cref="Faulted"/> because the two call for opposite reactions. A fault is a defect
    /// on this side — a parser that no longer follows the markup, a socket that died — and the right answer is
    /// to look at it. A refusal is the engine asking to be left alone, and the right answer is to leave it
    /// alone for a while, which is what <see cref="ISearchCooldown"/> does with it. Folding the two together,
    /// as the search once did, would have rested an engine over our own bug.
    /// </remarks>
    Refused
}

/// <summary>
/// The reason an attempt produced no results.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="SearchFailureKind"/> alone because a fault is only actionable with the message
/// the provider threw, while <see cref="SearchFailureKind.NoResults"/> has nothing to add.
/// </remarks>
public sealed record SearchFailure(SearchFailureKind Kind, string? Message = null);
