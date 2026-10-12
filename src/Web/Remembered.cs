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
internal sealed class Remembered<TValue>(
    TimeSpan forgetAfter,
    Func<TValue, DateTimeOffset, bool> isStale,
    TimeProvider clock)
    where TValue : class
{
    private readonly ConcurrentDictionary<string, Kept> _kept = new(StringComparer.OrdinalIgnoreCase);
    private long _sweepDue = DateTimeOffset.MinValue.UtcTicks;

    /// <summary>How many hosts are currently remembered.</summary>
    public int Count => _kept.Count;

    /// <summary>What is known about this host, without remembering it if nothing is.</summary>
    public bool TryGet(string key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_kept.TryGetValue(key, out var kept))
        {
            kept.Asked(clock.GetUtcNow());

            value = kept.Value;

            return true;
        }

        value = null;

        return false;
    }

    /// <summary>What is known about this host, remembering it the first time it is asked about.</summary>
    public TValue Of(string key, Func<string, TValue> create)
    {
        if (_kept.TryGetValue(key, out var known))
        {
            known.Asked(clock.GetUtcNow());

            return known.Value;
        }

        Sweep();

        return _kept.GetOrAdd(key, name => new Kept(create(name), clock.GetUtcNow())).Value;
    }

    /// <summary>Forgets this host, unless what is remembered about it has been replaced since it was read.</summary>
    public bool TryForget(string key, TValue known)
    {
        if (!_kept.TryGetValue(key, out var kept) || !ReferenceEquals(kept.Value, known))
            return false;

        return _kept.TryRemove(new KeyValuePair<string, Kept>(key, kept));
    }

    /// <summary>Forgets every host.</summary>
    public void Clear() => _kept.Clear();

    /// <summary>
    /// Drops what is no longer worth keeping, and never what has only just arrived.
    /// </summary>
    /// <remarks>
    /// The residency check is the fix for a defect this class had from the day it was written, and it is
    /// worth stating because the predicate looks like it already covers it and does not. <see cref="Of"/>
    /// hands back a value with nothing written to it yet — a gate holding nobody, an entry whose read has
    /// not started — which is exactly the state <c>isStale</c> is looking for. The next arrival
    /// of an unknown key therefore swept it away before its owner had written anything, and the owner's
    /// write then landed on an object the map no longer held.
    /// <para>
    /// MEASURED on 2026-09-15: sixty-four callers, fifty rounds, <c>ForgetHostAfter</c> zero — 3196 occasions
    /// on which two callers were inside one host at once, each correctly holding the only slot of a
    /// different gate. <c>MaxConcurrentPerHost</c> is the setting that exists to make that impossible.
    /// </para>
    /// <para>
    /// The window is measured from the last time the entry was ASKED FOR, not from when it was made, and that
    /// difference was read on 2026-09-20. Measured from creation, the rule protects a value made a moment ago
    /// and nothing else — so a host last visited an hour ago and visited again NOW was swept out from under the
    /// caller it had just been handed to, whose rest then landed on an object the map no longer held and whose
    /// successor built a second gate for the same host. Being handed to a caller is the evidence that somebody
    /// has been to this host lately, which is the whole of what the window asks.
    /// </para>
    /// <para>
    /// A value younger than <c>forgetAfter</c> is kept whatever the predicate says, because that
    /// is what the name already promises. It closes every configuration but one: a forgetting of zero is
    /// eager by request, and no rule here can hold against it — a gate is stale the instant it is made.
    /// Start-up validation refuses that setting instead.
    /// </para>
    /// </remarks>
    private void Sweep()
    {
        var now = clock.GetUtcNow();
        var due = Interlocked.Read(ref _sweepDue);

        if (now.UtcTicks < due)
            return;

        if (Interlocked.CompareExchange(ref _sweepDue, (now + forgetAfter / 4).UtcTicks, due) != due)
            return;

        var since = now - forgetAfter;

        foreach (var (key, kept) in _kept)
        {
            if (kept.AskedAt <= since && isStale(kept.Value, since))
                TryForget(key, kept.Value);
        }
    }

    /// <summary>What is remembered about one host, and when anybody last asked for it.</summary>
    /// <remarks>
    /// The instant moves only forwards and is written under <see cref="Interlocked"/>, because every reader of
    /// the map writes it: two callers arriving together must not leave it holding half of one instant beside
    /// half of another, and the older of two answers is never the one to keep.
    /// </remarks>
    private sealed class Kept(TValue value, DateTimeOffset at)
    {
        private long _askedAt = at.UtcTicks;

        public TValue Value => value;

        public DateTimeOffset AskedAt => new(Interlocked.Read(ref _askedAt), TimeSpan.Zero);

        public void Asked(DateTimeOffset at)
        {
            var moved = at.UtcTicks;
            var standing = Interlocked.Read(ref _askedAt);

            while (moved > standing)
            {
                var found = Interlocked.CompareExchange(ref _askedAt, moved, standing);

                if (found == standing)
                    return;

                standing = found;
            }
        }
    }
}
