using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Politeness;
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
    IPoliteness politeness,
    TimeProvider clock,
    IOptions<SearchOptions> options,
    ILogger<WebSearch> logger) : IWebSearch
{
    private readonly IReadOnlyList<ISearchProvider> _providers = [.. providers];
    private readonly SearchOptions _options = options.Value;
    private readonly Lock _round = new();
    private int _next;

    /// <inheritdoc />
    public Task<SearchOutcome> FindAsync(IEnumerable<string> queries, CancellationToken ct = default)
        => GatheredAsync(StreamAsync(queries, ct));

    /// <inheritdoc />
    public Task<SearchOutcome> FindAsync(IEnumerable<string> queries, TimeSpan budget, CancellationToken ct = default)
        => GatheredAsync(StreamAsync(queries, budget, ct));

    private static async Task<SearchOutcome> GatheredAsync(IAsyncEnumerable<SearchAttempt> streamed)
    {
        var attempts = new List<SearchAttempt>();
        var results = new List<SearchResult>();

        await foreach (var attempt in streamed)
        {
            attempts.Add(attempt);
            results.AddRange(attempt.Results);
        }

        return new SearchOutcome(Deduplicated(results), attempts);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The validation stays in a method that is not an iterator, and the walk lives in the one below it: an
    /// iterator defers its whole body to the first enumeration, so a caller handed a null list of queries
    /// would learn about it later and somewhere else.
    /// </remarks>
    public IAsyncEnumerable<SearchAttempt> StreamAsync(
        IEnumerable<string> queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queries);

        return AskedAsync([.. queries], budget: null, ct);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SearchAttempt> StreamAsync(
        IEnumerable<string> queries,
        TimeSpan budget,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(budget, TimeSpan.Zero);

        return AskedAsync([.. queries], budget, ct);
    }

    /// <summary>Every attempt this search makes, and every one it declines to make.</summary>
    /// <remarks>
    /// The resting providers are yielded first and cost nothing: they are what the search decided not to do,
    /// one entry per provider and query like every other attempt, so a caller folding attempts into a report
    /// counts them the same way. They are not recorded in the metrics, which count requests made.
    /// </remarks>
    private async IAsyncEnumerable<SearchAttempt> AskedAsync(
        IReadOnlyList<string> queries,
        TimeSpan? budget,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var resting = Resting();
        var awake = Awake(resting);

        foreach (var query in queries)
        {
            foreach (var (provider, until) in resting)
                yield return Unasked(provider, until, query);
        }

        // Which engines are worth a place is decided as each query's turn is drawn rather than once for the
        // batch, because the answer changes while the batch runs: the engine that refused the first query
        // cannot answer the second, and the one whose parser is broken cannot answer any of them.
        var faulted = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        var work = queries.SelectMany(query =>
            Turn(awake, faulted).Select(provider => (Provider: provider, Query: query)));

        // The budget's own source runs on the same clock the deadline below is read from, so a deployment or a
        // test that keeps its own TimeProvider is obeyed by both halves of the same promise. It is deliberately
        // NOT linked to the caller's token: what it means is "the budget ran out", and the caller cancelling is
        // a different sentence that the fan-out already tells.
        using var timed = budget is { } limit ? new CancellationTokenSource(limit, clock) : null;

        var deadline = budget is { } span ? clock.GetUtcNow() + span : (DateTimeOffset?)null;
        var spent = timed?.Token ?? CancellationToken.None;

        // The fan-out itself runs under the CALLER's token and the attempts under the budget's. Handing the
        // budget to the fan-out instead ends the whole enumeration the moment it fires — measured on
        // 2026-09-15, the first budget test died inside the channel read with the attempts it was meant to
        // report still unwritten. What the budget may do is cut an attempt short; ending the search is the
        // caller's alone.
        //
        // An attempt ALSO listens to the fan-out's own token, which fires when the caller stops reading. Until
        // 2026-09-20 that token was discarded, and a caller that broke out of the loop — the documented way to
        // take the cheap answers and skip the dear one — was held five seconds while the dear one ran on.
        var asked = Fanout.OverAsync(
            work,
            async (item, fanned) =>
            {
                using var both = CancellationTokenSource.CreateLinkedTokenSource(fanned, spent);

                var attempt = await AffordableAsync(item.Provider, item.Query, deadline, spent, both.Token);

                if (attempt.Failure?.Kind is SearchFailureKind.Faulted)
                    faulted.TryAdd(item.Provider.Name, 0);

                return attempt;
            },
            Unexpected,
            _options.MaxDegreeOfParallelism,
            ct);

        await foreach (var attempt in asked.WithCancellation(ct))
            yield return attempt;
    }

    /// <summary>Asks a provider, unless it rests or the budget cannot reach it — both known before the request.</summary>
    /// <remarks>
    /// The check is made as each pair starts rather than when the work is planned, because the gate moves
    /// while a search runs: an engine that was open when the batch began is closed a moment later by the
    /// query ahead of it, and a decision made at planning time would be wrong for every pair but the first.
    /// <para>
    /// The same holds for a rest, and until 2026-09-20 this reasoning was applied to the gate and not to it:
    /// the resting set was read once per batch, so an engine that refused the first query was asked every
    /// query after it. Measured that day — five queries, five requests to an engine that had already said no.
    /// </para>
    /// <para>
    /// A search without a budget is bounded all the same, by <see cref="SearchOptions.Timeout"/> — the longest
    /// one request may take. An engine whose host opens after that could only be asked in order to be timed
    /// out, and hearing when it opens is worth more than spending the time to be told nothing.
    /// </para>
    /// <para>
    /// A provider that names no host is always asked. It is not a promise that it is quick — it is that this
    /// library knows nothing about its cost, and refusing to ask on a guess would be worse than paying it.
    /// </para>
    /// </remarks>
    private async ValueTask<SearchAttempt> AffordableAsync(
        ISearchProvider provider,
        string query,
        DateTimeOffset? deadline,
        CancellationToken spent,
        CancellationToken token)
    {
        if (cooldown.RestingUntil(provider.Name) is { } rest)
            return Unasked(provider.Name, rest, query);

        var now = clock.GetUtcNow();

        if (deadline is { } end && now >= end)
            return Skipped(provider, query, SearchFailure.OutOfTime(provider.Name));

        if (OpensAt(provider) is { } opens)
        {
            if (deadline is { } budget && opens > budget)
                return Skipped(provider, query, SearchFailure.TooLate(provider.Name, opens));

            if (opens > now + _options.Timeout)
                return Skipped(provider, query, SearchFailure.Shut(provider.Name, opens, _options.Timeout));
        }

        return await AttemptAsync(provider, query, spent, token);
    }

    /// <summary>When the host this provider talks to next opens, as far as the gate knows.</summary>
    private DateTimeOffset? OpensAt(ISearchProvider provider)
        => provider.Host is { Length: > 0 } host && Uri.TryCreate($"https://{host}", UriKind.Absolute, out var site)
            ? politeness.OpensAt(site)
            : null;

    private static SearchAttempt Skipped(ISearchProvider provider, string query, SearchFailure failure)
        => new()
        {
            Provider = provider.Name,
            Query = query,
            Duration = TimeSpan.Zero,
            Failure = failure
        };

    /// <summary>What a provider that was left out of this search contributes to its answer.</summary>
    private static SearchAttempt Unasked(string provider, DateTimeOffset until, string query)
        => new()
        {
            Provider = provider,
            Query = query,
            Duration = TimeSpan.Zero,
            Failure = SearchFailure.Resting(provider, until)
        };

    /// <summary>The attempt that could not even be recorded.</summary>
    /// <remarks>
    /// <see cref="AttemptAsync"/> answers every way a provider can fail as an attempt, so nothing should
    /// arrive here. What does arrive is a defect on this side, and it comes back as one attempt rather than
    /// ending a search that other providers were still answering.
    /// </remarks>
    private static SearchAttempt Unexpected((ISearchProvider Provider, string Query) item, Exception cause)
        => new()
        {
            Provider = item.Provider.Name,
            Query = item.Query,
            Duration = TimeSpan.Zero,
            Failure = new SearchFailure(SearchFailureKind.Faulted, cause.Message)
        };

    private static IReadOnlyList<SearchResult> Deduplicated(IEnumerable<SearchResult> results)
        => [.. results.GroupBy(result => result.Address).Select(group => group.First())];

    /// <summary>The providers resting right now, each with the instant its rest ends.</summary>
    private IReadOnlyList<(string Provider, DateTimeOffset Until)> Resting()
        =>
        [
            .. _providers
                .Select(provider => (Provider: provider.Name, Until: cooldown.RestingUntil(provider.Name)))
                .Where(rest => rest.Until is not null)
                .Select(rest => (rest.Provider, Until: rest.Until!.Value))
        ];

    /// <summary>The providers that may be asked right now, with the resting ones named in the log.</summary>
    /// <remarks>
    /// A search in which every provider rests asks nobody and comes back unanswered — which is the truth, and
    /// louder in the log than the usual line, because from the caller's side it looks like a search that found
    /// nothing. Since 2026-09-15 the caller is told the same thing in the answer rather than only here: the
    /// log is for whoever runs the process, and this is a fact about the search the process was asked to run.
    /// </remarks>
    private IReadOnlyList<ISearchProvider> Awake(IReadOnlyList<(string Provider, DateTimeOffset Until)> resting)
    {
        var asleep = resting.Select(rest => rest.Provider).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<ISearchProvider> awake = [.. _providers.Where(provider => !asleep.Contains(provider.Name))];

        if (asleep.Count > 0)
            logger.Log(
                awake.Count > 0 ? LogLevel.Information : LogLevel.Warning,
                "{Resting} left out of this search, resting after a refusal; {Count} provider(s) asked",
                string.Join(", ", asleep),
                awake.Count);

        return awake;
    }

    /// <summary>The providers one query goes to: the next few in the round, or every one when the round holds them all.</summary>
    /// <remarks>
    /// The round runs over the providers awake for this search, so a resting one is skipped rather than
    /// counted as a turn, and its share goes to the ones that can take it. A provider named in
    /// <see cref="SearchOptions.AlwaysAsked"/> stands outside the round and joins every query.
    /// <para>
    /// A PLACE IN THE ROUND GOES TO AN ENGINE THAT CAN USE IT. Measured by a consumer on 2026-09-16: four
    /// engines, two places per query — both places went to an engine resting after a refusal and one whose
    /// parser was faulting, and the engine that answers that subject well was never asked. So an engine that
    /// has refused, or that has already faulted in this batch, stands aside while a healthy one is free.
    /// </para>
    /// <para>
    /// It stands aside, it is not dropped: a fault is still asked for once, because resting an engine over our
    /// own defect is how the defect stops being visible, and when there are not enough healthy engines to fill
    /// the places the faulted ones take them again. Nothing here is remembered past this batch — a parser is
    /// mended by a release, not by a timer, and the next search finds out for itself.
    /// </para>
    /// </remarks>
    private IReadOnlyList<ISearchProvider> Turn(
        IReadOnlyList<ISearchProvider> awake,
        IReadOnlyDictionary<string, byte> faulted)
    {
        var healthy = awake
            .Where(provider => !faulted.ContainsKey(provider.Name) && cooldown.RestingUntil(provider.Name) is null)
            .ToArray();

        var able = healthy.Count(provider => !IsAlwaysAsked(provider)) >= _options.ProvidersPerQuery
            ? healthy
            : awake;

        var spared = able.Where(provider => !IsAlwaysAsked(provider)).ToArray();

        if (spared.Length <= _options.ProvidersPerQuery)
            return able;

        lock (_round)
        {
            var first = _next % spared.Length;

            _next = (first + _options.ProvidersPerQuery) % spared.Length;

            return
            [
                .. able.Where(IsAlwaysAsked),
                .. Enumerable.Range(first, _options.ProvidersPerQuery).Select(index => spared[index % spared.Length])
            ];
        }
    }

    private bool IsAlwaysAsked(ISearchProvider provider)
        => _options.AlwaysAsked.Contains(provider.Name, StringComparer.Ordinal);

    /// <summary>Asks one provider one query, and turns every way that can end into an attempt.</summary>
    /// <remarks>
    /// A cancellation has to say WHOSE it was: the budget running out is
    /// <see cref="SearchFailureKind.OutOfTime"/>, and everything else stays
    /// <see cref="SearchFailureKind.Canceled"/>. Told apart nowhere else, a deadline a deployment set would
    /// read exactly like a user closing a page. The condition is on the BUDGET's token, not on the one the work
    /// runs under: that one also fires when the caller stops reading, and a provider that timed out inside
    /// itself — its own client's clock, nothing to do with this search — must not be reported as the budget's
    /// doing either. A search without a budget has nothing that can run out.
    /// <para>
    /// A SECOND HALF WAS WRITTEN HERE AND TAKEN OUT, and the reason is recorded because it is the obvious
    /// thing to propose again: <c>&amp;&amp; !caller.IsCancellationRequested</c>, to keep a caller's own
    /// cancellation out of this arm. MEASURED BY REMOVAL on 2026-09-15 — no test could tell the two apart, and
    /// none can: when the caller cancels, the fan-out this attempt is written to is reading under that same
    /// token, so the enumeration ends and the attempt is never yielded to anybody. A condition nothing can
    /// observe is a claim about behaviour that does not exist.
    /// </para>
    /// </remarks>
    private async ValueTask<SearchAttempt> AttemptAsync(
        ISearchProvider provider,
        string query,
        CancellationToken spent,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            var answer = await provider.FindAsync(query, ct);

            return Judged(provider, query, Stopwatch.GetElapsedTime(started), answer);
        }
        catch (OperationCanceledException) when (spent.IsCancellationRequested)
        {
            var duration = Stopwatch.GetElapsedTime(started);

            logger.LogWarning("{Provider} ran past the budget for '{Query}' after {Ms:F0}ms",
                provider.Name, query, duration.TotalMilliseconds);

            return Record(provider, query, duration, "out of time") with
            {
                Failure = SearchFailure.OutOfTime(provider.Name)
            };
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
