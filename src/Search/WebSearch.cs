using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Search.Diagnostics;
using Snail.Toolkit.Web.Streaming;

namespace Snail.Toolkit.Web.Search;

/// <summary>
/// Runs a batch of queries across the registered providers, a few per query and in turn.
/// </summary>
/// <remarks>
/// Deduplication is why several providers are worth asking at once, and it is decided by <see cref="Link"/>
/// equality rather than by string comparison: grouping on the raw address kept the same page three times.
/// One provider failing is a recorded attempt, never an exception — a slow or blocked back end must not take
/// the whole answer with it. A provider that refused is recorded as refused rather than faulted, and is left
/// out of the following searches until its rest is over: the engines measured on 2026-09-12 closed over
/// frequency, not fingerprint, and asking a challenged engine again at once is how a pause becomes a ban.
/// For the same reason a query goes to <see cref="SearchOptions.ProvidersPerQuery"/> providers rather than to
/// all of them, the next query to the next ones in the round, so that a batch spreads what it asks.
/// </remarks>
public sealed class WebSearch(
    IEnumerable<ISearchProvider> providers,
    ISearchCooldown cooldown,
    IOptions<SearchOptions> options,
    ILogger<WebSearch> logger) : IWebSearch
{
    private readonly IReadOnlyList<ISearchProvider> _providers = [.. providers];
    private readonly SearchOptions _options = options.Value;
    private readonly Lock _round = new();
    private int _next;

    /// <inheritdoc />
    public async Task<SearchOutcome> FindAsync(IEnumerable<string> queries, CancellationToken ct = default)
    {
        var attempts = new List<SearchAttempt>();
        var results = new List<SearchResult>();

        await foreach (var attempt in StreamAsync(queries, ct))
        {
            attempts.Add(attempt);
            results.AddRange(attempt.Results);
        }

        return new SearchOutcome(Deduplicated(results), attempts);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SearchAttempt> StreamAsync(
        IEnumerable<string> queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queries);

        var wanted = queries.ToArray();
        var awake = Awake();
        var work = wanted.SelectMany(query => Turn(awake).Select(provider => (Provider: provider, Query: query)));

        return Fanout.OverAsync(
            work,
            async (item, token) => await AttemptAsync(item.Provider, item.Query, token),
            _options.MaxDegreeOfParallelism,
            ct);
    }

    private static IReadOnlyList<SearchResult> Deduplicated(IEnumerable<SearchResult> results)
        => [.. results.GroupBy(result => result.Address).Select(group => group.First())];

    /// <summary>The providers that may be asked right now, with the resting ones named in the log.</summary>
    /// <remarks>
    /// A search in which every provider rests asks nobody and comes back unanswered — which is the truth, and
    /// louder in the log than the usual line, because from the caller's side it looks like a search that found
    /// nothing.
    /// </remarks>
    private IReadOnlyList<ISearchProvider> Awake()
    {
        var byRest = _providers.ToLookup(provider => cooldown.IsResting(provider.Name));
        IReadOnlyList<ISearchProvider> awake = [.. byRest[false]];

        if (byRest[true].Any())
            logger.Log(
                awake.Count > 0 ? LogLevel.Information : LogLevel.Warning,
                "{Resting} left out of this search, resting after a refusal; {Count} provider(s) asked",
                string.Join(", ", byRest[true].Select(provider => provider.Name)),
                awake.Count);

        return awake;
    }

    /// <summary>The providers one query goes to: the next few in the round, or every one when the round holds them all.</summary>
    /// <remarks>
    /// The round runs over the providers awake for this search, so a resting one is skipped rather than
    /// counted as a turn, and its share goes to the ones that can take it. A provider named in
    /// <see cref="SearchOptions.AlwaysAsked"/> stands outside the round and joins every query.
    /// </remarks>
    private IReadOnlyList<ISearchProvider> Turn(IReadOnlyList<ISearchProvider> awake)
    {
        var spared = awake.Where(provider => !IsAlwaysAsked(provider)).ToArray();

        if (spared.Length <= _options.ProvidersPerQuery)
            return awake;

        lock (_round)
        {
            var first = _next % spared.Length;

            _next = (first + _options.ProvidersPerQuery) % spared.Length;

            return
            [
                .. awake.Where(IsAlwaysAsked),
                .. Enumerable.Range(first, _options.ProvidersPerQuery).Select(index => spared[index % spared.Length])
            ];
        }
    }

    private bool IsAlwaysAsked(ISearchProvider provider)
        => _options.AlwaysAsked.Contains(provider.Name, StringComparer.Ordinal);

    private async ValueTask<SearchAttempt> AttemptAsync(
        ISearchProvider provider,
        string query,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            var answer = await provider.FindAsync(query, ct);

            return Judged(provider, query, Stopwatch.GetElapsedTime(started), answer);
        }
        catch (OperationCanceledException)
        {
            var duration = Stopwatch.GetElapsedTime(started);

            logger.LogWarning("{Provider} was cancelled for '{Query}' after {Ms:F0}ms",
                provider.Name, query, duration.TotalMilliseconds);

            return Record(provider, query, duration, "cancelled") with
            {
                Failure = new SearchFailure(SearchFailureKind.Canceled)
            };
        }
        catch (Exception exception)
        {
            var duration = Stopwatch.GetElapsedTime(started);

            logger.LogError(exception, "{Provider} threw for '{Query}' after {Ms:F0}ms",
                provider.Name, query, duration.TotalMilliseconds);

            return Record(provider, query, duration, "faulted") with
            {
                Failure = new SearchFailure(SearchFailureKind.Faulted, exception.Message)
            };
        }
    }

    /// <summary>Reads what a provider answered, and decides what that costs the provider.</summary>
    /// <remarks>
    /// A refusal rests the engine and a fault does not: a fault is a defect on this side — a parser that no
    /// longer follows the markup — and resting the engine over it would hide the one thing that needed fixing.
    /// An answer of any kind, with results or without, ends a rest.
    /// </remarks>
    private SearchAttempt Judged(ISearchProvider provider, string query, TimeSpan duration, SearchAnswer answer)
    {
        if (!answer.IsSuccess)
            return Unanswered(provider, query, duration, answer.Failure);

        cooldown.Answered(provider.Name);

        if (answer.Value.Count == 0)
        {
            logger.LogDebug("{Provider} found nothing for '{Query}' in {Ms:F0}ms",
                provider.Name, query, duration.TotalMilliseconds);

            return Record(provider, query, duration, "empty") with
            {
                Failure = new SearchFailure(SearchFailureKind.NoResults)
            };
        }

        logger.LogInformation("{Provider} found {Count} links for '{Query}' in {Ms:F0}ms",
            provider.Name, answer.Value.Count, query, duration.TotalMilliseconds);

        return Record(provider, query, duration, "ok") with { Results = answer.Value };
    }

    private SearchAttempt Unanswered(
        ISearchProvider provider,
        string query,
        TimeSpan duration,
        SearchFailure failure)
    {
        if (failure.Kind is not SearchFailureKind.Refused)
        {
            logger.LogError("{Provider} failed for '{Query}' after {Ms:F0}ms: {Reason}",
                provider.Name, query, duration.TotalMilliseconds, failure.Message);

            return Record(provider, query, duration, "faulted") with { Failure = failure };
        }

        var rest = cooldown.Refused(provider.Name);

        logger.LogWarning("{Provider} refused '{Query}' after {Ms:F0}ms and is left alone for {Rest}: {Reason}",
            provider.Name, query, duration.TotalMilliseconds, rest, failure.Message);

        return Record(provider, query, duration, "refused") with { Failure = failure };
    }

    private static SearchAttempt Record(ISearchProvider provider, string query, TimeSpan duration, string outcome)
    {
        var tags = new TagList
        {
            { "search.provider", provider.Name },
            { "outcome", outcome }
        };

        SearchDiagnostics.Attempts.Add(1, tags);
        SearchDiagnostics.Duration.Record(duration.TotalMilliseconds, tags);

        return new SearchAttempt
        {
            Provider = provider.Name,
            Query = query,
            Duration = duration
        };
    }
}
