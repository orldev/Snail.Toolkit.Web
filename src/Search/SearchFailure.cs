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
    Canceled
}

/// <summary>
/// The reason an attempt produced no results.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="SearchFailureKind"/> alone because a fault is only actionable with the message
/// the provider threw, while <see cref="SearchFailureKind.NoResults"/> has nothing to add.
/// </remarks>
public sealed record SearchFailure(SearchFailureKind Kind, string? Message = null);
