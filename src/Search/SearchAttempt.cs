namespace Snail.Toolkit.Web.Search;

/// <summary>
/// What one provider did with one query.
/// </summary>
/// <remarks>
/// Attempts travel back beside the results because an empty result set is ambiguous on its own: it reads the
/// same whether nothing matched or every provider was down.
/// </remarks>
public sealed record SearchAttempt
{
    /// <summary>Gets the provider that was asked.</summary>
    public required string Provider { get; init; }

    /// <summary>Gets the query it was asked.</summary>
    public required string Query { get; init; }

    /// <summary>Gets how long the attempt took.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Gets the links the provider offered.</summary>
    public IReadOnlyList<SearchResult> Results { get; init; } = [];

    /// <summary>Gets why the attempt carried nothing, or <see langword="null"/> when it carried something.</summary>
    public SearchFailure? Failure { get; init; }

    /// <summary>Gets whether the provider answered, whether or not the answer held anything.</summary>
    public bool IsAnswered => Failure is null or { Kind: SearchFailureKind.NoResults };
}
