using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Robots;

/// <summary>
/// Caches the rules of every origin visited, one read per origin.
/// </summary>
/// <remarks>
/// Caching lives here rather than in the transport so that both engines age entries identically: a crawl that
/// re-read <c>robots.txt</c> per URL would announce itself twice per page, and one that never re-read it would
/// obey a year-old file.
/// </remarks>
public sealed class SiteRules(IRobotsSource source, IOptions<RobotsOptions> options) : ISiteRules
{
    private readonly RobotsOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private long _sweepDue = DateTimeOffset.MinValue.UtcTicks;

    /// <summary>How many origins are currently remembered.</summary>
    internal int RememberedOrigins => _cache.Count;

    /// <inheritdoc />
    public async ValueTask<bool> IsAllowedAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!_options.IsObeyed)
            return true;

        var rules = await RulesAsync(url, proxy, ct);

        return rules.IsAllowed(url.PathAndQuery, _options.UserAgent);
    }

    /// <inheritdoc />
    public async ValueTask<TimeSpan?> DelayAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!_options.IsObeyed)
            return null;

        var rules = await CachedAsync(url, proxy, ct);

        return rules.CrawlDelay(_options.UserAgent);
    }

    /// <inheritdoc />
    public async ValueTask<RobotsFile> RulesAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        return await CachedAsync(url, proxy, ct);
    }

    private async Task<RobotsFile> CachedAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct)
    {
        var origin = new Uri($"{url.Scheme}://{url.Authority}");

        while (true)
        {
            var entry = EntryFor(origin, proxy);

            Cached cached;

            try
            {
                cached = await entry.Read.Value.WaitAsync(ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _cache.TryRemove(new KeyValuePair<string, Entry>(origin.AbsoluteUri, entry));
                throw;
            }

            if (DateTimeOffset.UtcNow - cached.ReadAt <= _options.CacheLifetime)
                return cached.Rules;

            _cache.TryRemove(new KeyValuePair<string, Entry>(origin.AbsoluteUri, entry));
        }
    }

    /// <summary>This origin's entry, forgetting the ones that have outlived their lifetime first.</summary>
    /// <remarks>
    /// An entry was only ever dropped by someone asking for that same origin again, so a crawl across many
    /// sites kept every file it had ever read for as long as the process ran — including the ones that had
    /// expired an hour earlier and would never be asked for again. The map now holds the origins read within
    /// <see cref="RobotsOptions.CacheLifetime"/>, which is exactly the set an answer can still come from.
    /// <para>
    /// Sweeping happens when an unknown origin arrives, and at most once a quarter of that lifetime: a sweep
    /// reads the whole map, and doing that per new origin would be quadratic over a crawl.
    /// </para>
    /// </remarks>
    private Entry EntryFor(Uri origin, ProxyEndpoint? proxy)
    {
        if (_cache.TryGetValue(origin.AbsoluteUri, out var known))
            return known;

        ForgetExpired();

        return _cache.GetOrAdd(origin.AbsoluteUri, _ => new Entry(Deferred(origin, proxy), DateTimeOffset.UtcNow));
    }

    /// <summary>Drops the entries no answer could come from any more.</summary>
    /// <remarks>
    /// Age is counted from when the read started rather than when it finished, which is the earlier of the
    /// two and so forgets an entry a download's worth of time early. That costs at most one extra fetch, and
    /// only for an origin nobody asked about for a whole lifetime — which was going to be re-read anyway.
    /// </remarks>
    private void ForgetExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var due = Interlocked.Read(ref _sweepDue);

        if (now.UtcTicks < due)
            return;

        if (Interlocked.CompareExchange(ref _sweepDue, (now + _options.CacheLifetime / 4).UtcTicks, due) != due)
            return;

        foreach (var (origin, entry) in _cache)
            if (now - entry.StartedAt > _options.CacheLifetime)
                _cache.TryRemove(new KeyValuePair<string, Entry>(origin, entry));
    }

    /// <summary>Starts one read per origin, detached from any single caller's cancellation.</summary>
    /// <remarks>
    /// The caller's token governs its own wait, never the shared read. Capturing it here would let the first
    /// caller to give up cancel the download for everyone and cache the cancelled task as the answer for the
    /// whole lifetime of the entry.
    /// </remarks>
    private Lazy<Task<Cached>> Deferred(Uri origin, ProxyEndpoint? proxy)
        => new(() => ReadAsync(origin, proxy), LazyThreadSafetyMode.ExecutionAndPublication);

    private async Task<Cached> ReadAsync(Uri origin, ProxyEndpoint? proxy)
    {
        var rules = await source.ReadAsync(origin, proxy, CancellationToken.None);

        return new Cached(rules, DateTimeOffset.UtcNow);
    }

    private sealed record Cached(RobotsFile Rules, DateTimeOffset ReadAt);

    private sealed record Entry(Lazy<Task<Cached>> Read, DateTimeOffset StartedAt);
}
