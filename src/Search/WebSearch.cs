using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Search.Diagnostics;
using Snail.Toolkit.Web.Streaming;

namespace Snail.Toolkit.Web.Search;

/// <summary>
/// Runs a batch of queries across every registered provider.
/// </summary>
/// <remarks>
/// Deduplication is why several providers are worth asking at once, and it is decided by <see cref="Link"/>
/// equality rather than by string comparison: grouping on the raw address kept the same page three times.
/// One provider failing is a recorded attempt, never an exception — a slow or blocked back end must not take
/// the whole answer with it.
/// </remarks>
public sealed class WebSearch(
    IEnumerable<ISearchProvider> providers,
    IOptions<SearchOptions> options,
    ILogger<WebSearch> logger) : IWebSearch
{
    private readonly IReadOnlyList<ISearchProvider> _providers = [.. providers];
    private readonly SearchOptions _options = options.Value;

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
        var work = _providers.SelectMany(provider => wanted.Select(query => (Provider: provider, Query: query)));

        return Fanout.OverAsync(
            work,
            async (item, token) => await AttemptAsync(item.Provider, item.Query, token),
            _options.MaxDegreeOfParallelism,
            ct);
    }

    private static IReadOnlyList<SearchResult> Deduplicated(IEnumerable<SearchResult> results)
        => [.. results.GroupBy(result => result.Address).Select(group => group.First())];

    private async ValueTask<SearchAttempt> AttemptAsync(
        ISearchProvider provider,
        string query,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            var results = await provider.FindAsync(query, ct);
            var duration = Stopwatch.GetElapsedTime(started);

            if (results.Count == 0)
            {
                logger.LogDebug("{Provider} found nothing for '{Query}' in {Ms:F0}ms",
                    provider.Name, query, duration.TotalMilliseconds);

                return Record(provider, query, duration, "empty") with
                {
                    Failure = new SearchFailure(SearchFailureKind.NoResults)
                };
            }

            logger.LogInformation("{Provider} found {Count} links for '{Query}' in {Ms:F0}ms",
                provider.Name, results.Count, query, duration.TotalMilliseconds);

            return Record(provider, query, duration, "ok") with { Results = results };
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

            logger.LogError(exception, "{Provider} failed for '{Query}' after {Ms:F0}ms",
                provider.Name, query, duration.TotalMilliseconds);

            return Record(provider, query, duration, "faulted") with
            {
                Failure = new SearchFailure(SearchFailureKind.Faulted, exception.Message)
            };
        }
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
