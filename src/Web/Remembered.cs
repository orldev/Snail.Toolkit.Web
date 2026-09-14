using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Snail.Toolkit.Web;

/// <summary>
/// What is kept per host, and the forgetting that keeps it from growing for as long as the process runs.
/// </summary>
/// <typeparam name="TValue">What is remembered about one host.</typeparam>
/// <remarks>
/// Two maps in this package answer questions about hosts — how fast one may be asked, and what its
/// <c>robots.txt</c> said — and both had the same defect for the same reason: the hosts of the web are not a
/// finite set, and neither map ever removed an entry. They were given the same cure on 2026-09-14 and it was
/// written twice, which is how the cure becomes the next defect.
/// <para>
/// The subtle part is the part worth having once. Forgetting happens on the arrival of an UNKNOWN key, because
/// that is the only moment the map grows; it is throttled to a quarter of the window, because a sweep reads
/// the whole map and doing that per new key would be quadratic over a crawl; and the throttle is claimed with
/// a compare-and-exchange, so many callers arriving together produce one sweep rather than one each.
/// </para>
/// <para>
/// What counts as no longer worth keeping is the caller's to say, because the two maps disagree about it: a
/// pacing gate may only be dropped when nobody is inside it and it is holding nobody back, while a cached file
/// may be dropped the moment no answer could come from it any more.
/// </para>
/// </remarks>
internal sealed class Remembered<TValue>(TimeSpan forgetAfter, Func<TValue, DateTimeOffset, bool> isStale)
    where TValue : class
{
    private readonly ConcurrentDictionary<string, TValue> _kept = new(StringComparer.OrdinalIgnoreCase);
    private long _sweepDue = DateTimeOffset.MinValue.UtcTicks;

    /// <summary>How many hosts are currently remembered.</summary>
    public int Count => _kept.Count;

    /// <summary>What is known about this host, without remembering it if nothing is.</summary>
    public bool TryGet(string key, [MaybeNullWhen(false)] out TValue value) => _kept.TryGetValue(key, out value);

    /// <summary>What is known about this host, remembering it the first time it is asked about.</summary>
    public TValue Of(string key, Func<string, TValue> create)
    {
        if (_kept.TryGetValue(key, out var known))
            return known;

        Sweep();

        return _kept.GetOrAdd(key, create);
    }

    /// <summary>Forgets this host, unless what is remembered about it has been replaced since it was read.</summary>
    public bool Forget(string key, TValue known)
        => _kept.TryRemove(new KeyValuePair<string, TValue>(key, known));

    /// <summary>Forgets every host.</summary>
    public void Clear() => _kept.Clear();

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        var due = Interlocked.Read(ref _sweepDue);

        if (now.UtcTicks < due)
            return;

        if (Interlocked.CompareExchange(ref _sweepDue, (now + forgetAfter / 4).UtcTicks, due) != due)
            return;

        var since = now - forgetAfter;

        foreach (var (key, value) in _kept)
        {
            if (isStale(value, since))
                Forget(key, value);
        }
    }
}
