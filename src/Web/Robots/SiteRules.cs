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
    private readonly ConcurrentDictionary<string, Lazy<Task<Cached>>> _cache = new(StringComparer.OrdinalIgnoreCase);

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
            var entry = _cache.GetOrAdd(origin.AbsoluteUri, _ => Deferred(origin, proxy));

            Cached cached;

            try
            {
                cached = await entry.Value.WaitAsync(ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _cache.TryRemove(new KeyValuePair<string, Lazy<Task<Cached>>>(origin.AbsoluteUri, entry));
                throw;
            }

            if (DateTimeOffset.UtcNow - cached.ReadAt <= _options.CacheLifetime)
                return cached.Rules;

            _cache.TryRemove(new KeyValuePair<string, Lazy<Task<Cached>>>(origin.AbsoluteUri, entry));
        }
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
}
