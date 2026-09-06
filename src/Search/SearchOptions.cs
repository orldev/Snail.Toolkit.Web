namespace Snail.Toolkit.Web.Search;

/// <summary>
/// How many providers are asked at once and how much each may answer.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Search";

    /// <summary>Gets or sets how many provider-and-query pairs may be in flight at once.</summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>Gets or sets how many results one provider may contribute to one query.</summary>
    /// <remarks>
    /// A ceiling per provider rather than per answer, so one talkative provider cannot crowd out the rest
    /// after deduplication.
    /// </remarks>
    public int MaxResultsPerProvider { get; set; } = 30;

    /// <summary>Gets or sets how long one provider request may take, retries included.</summary>
    /// <remarks>
    /// Spent by the resilience pipeline rather than by <see cref="HttpClient.Timeout"/>, and divided into a
    /// per-attempt budget and a circuit-breaker sampling window. The circuit breaker refuses a window shorter
    /// than 500 ms, which is what puts a floor of one second under this setting; anything less is refused when
    /// the host starts rather than surfacing as a pipeline validation error on the first query.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
