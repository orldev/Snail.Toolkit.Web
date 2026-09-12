namespace Snail.Toolkit.Web.Search;

/// <summary>
/// How many providers are asked at once, how much each may answer, and how long a refused one is left alone.
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

    /// <summary>Gets or sets how long a provider is left alone after its first refusal.</summary>
    /// <remarks>
    /// A challenge page is not an error in the query but a reaction to how often it was asked: measured on
    /// 2026-09-12, one engine answered, challenged, answered, challenged and answered again over one evening,
    /// with nothing changing between the requests but their spacing. Each further refusal doubles the rest
    /// until it reaches <see cref="MaxRefusalCooldown"/>; any answer, with results or without, ends it.
    /// </remarks>
    public TimeSpan RefusalCooldown { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the longest rest a run of refusals can grow to.</summary>
    public TimeSpan MaxRefusalCooldown { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Gets or sets how many providers one query is sent to.</summary>
    /// <remarks>
    /// The round, not the set: with four engines registered and two here, one query goes to two of them and
    /// the next query to the other two, so a batch spreads its load instead of landing on everybody at once.
    /// Completeness survives it — results are merged and deduplicated across the batch — and the engines live
    /// longer, which is what the measured ones were lost to. A number at or above the count of registered
    /// providers asks all of them, as the search always did.
    /// </remarks>
    public int ProvidersPerQuery { get; set; } = 2;

    /// <summary>Gets or sets the providers asked on every query, outside the round.</summary>
    /// <remarks>
    /// The round exists to spare engines that close over frequency, and a provider that costs nothing to ask
    /// — a local index — has nothing to be spared: left to the round, its answer would be a matter of luck.
    /// Named rather than typed, because whether a provider is cheap is a fact about the deployment.
    /// </remarks>
    public List<string> AlwaysAsked { get; set; } = [];
}
