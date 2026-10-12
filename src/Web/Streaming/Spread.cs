namespace Snail.Toolkit.Web.Streaming;

/// <summary>
/// Reorders work so that the items in flight belong to as many different hosts as possible.
/// </summary>
/// <remarks>
/// Throughput here is the number of DIFFERENT hosts being read at once, not the number of workers. A host is
/// held to one request at a time and a gap between them, so four workers given a thousand addresses of three
/// sites all queue at three gates and the run takes as long as the slowest of the three queues — the workers
/// beyond the third do nothing but wait. Reading the same thousand addresses host by host in turn keeps every
/// worker on a different site and finishes in a third of the time, without asking any site for anything faster.
/// <para>
/// Nothing here paces anything: the gate still decides when a host may be asked, and this only decides what to
/// offer it next. Being approximate is therefore free — the worst a wrong guess costs is a worker queueing at
/// a gate, which is what it did for every item before.
/// </para>
/// <para>
/// Told when each host opens, it prefers the one that opens soonest, and that is what keeps a run from
/// stopping while nothing is wrong with it: four workers handed four hosts that have just answered <c>429</c>
/// sit out the rest together while a hundred other sites go unread. Told nothing, it simply goes round, which
/// is still the whole of the gain on a list that holds no refusals.
/// </para>
/// </remarks>
internal static class Spread
{
    /// <summary>How many items may be held back while looking for a different host, per host wanted.</summary>
    /// <remarks>
    /// A bound on memory rather than a tuning knob, and the reason it can be stated so casually is that the
    /// only source which reaches it is one whose items really are all the same host — where there is nothing
    /// to interleave and the order this returns is the order it was given. With the default ceiling it holds
    /// back at most two hundred and fifty-six addresses.
    /// </remarks>
    private const int HeldBackPerHost = 64;

    /// <summary>The same items, ordered so that consecutive ones come from different hosts where they can.</summary>
    /// <remarks>
    /// Reading ahead stops as soon as <paramref name="wanted"/> different hosts are in hand, so a source that
    /// arrives already mixed is barely buffered at all, and an infinite one is never drained. Items of one host
    /// keep the order they arrived in: a crawl that put the newest first still reads the newest of each site
    /// first.
    /// </remarks>
    public static IEnumerable<T> OverHosts<T>(
        IEnumerable<T> source,
        Func<T, string> host,
        int wanted,
        Func<T, DateTimeOffset>? opensAt = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(host);

        return Interleaved(source, host, Math.Max(1, wanted), opensAt);
    }

    private static IEnumerable<T> Interleaved<T>(
        IEnumerable<T> source,
        Func<T, string> host,
        int wanted,
        Func<T, DateTimeOffset>? opensAt)
    {
        var waiting = new Dictionary<string, Queue<T>>(StringComparer.OrdinalIgnoreCase);
        var hosts = new List<string>();
        var held = 0;
        var cursor = 0;

        using var arriving = source.GetEnumerator();

        while (true)
        {
            Emptied(waiting, hosts, ref cursor);

            while (held < wanted * HeldBackPerHost
                   && ShouldReadAhead(waiting, hosts, opensAt, wanted)
                   && arriving.MoveNext())
            {
                var name = host(arriving.Current);

                if (!waiting.TryGetValue(name, out var queue))
                {
                    queue = new Queue<T>();
                    waiting[name] = queue;
                    hosts.Add(name);
                }

                queue.Enqueue(arriving.Current);
                held++;
            }

            if (held == 0)
                yield break;

            yield return Next(waiting, hosts, opensAt, ref cursor);

            held--;
        }
    }

    /// <summary>Whether more should be read before choosing, counting only the hosts that could be used now.</summary>
    /// <remarks>
    /// The clock here is the system's, where the rest of the toolkit reads an injected
    /// <see cref="TimeProvider"/> since 2026-09-20, and that is deliberate: this decides what to OFFER a
    /// worker next, never whether it may go, so being wrong costs a worker queueing at a gate — which is what
    /// it did for every item before this existed. Nothing observes the difference, so nothing could test it.
    /// <para>
    /// Counting hosts rather than usable hosts is what the first version did, and it reduced the whole thing
    /// to nothing in the case it was written for: with three workers and three sites resting at the head of
    /// the list, three hosts were in hand and two of them shut, so at most one worker could be given anything.
    /// Measured on 2026-09-14 — two pages read in the window where eighteen were waiting.
    /// </para>
    /// </remarks>
    private static bool ShouldReadAhead<T>(
        Dictionary<string, Queue<T>> waiting,
        List<string> hosts,
        Func<T, DateTimeOffset>? opensAt,
        int wanted)
    {
        if (opensAt is null)
            return hosts.Count < wanted;

        var now = DateTimeOffset.UtcNow;
        var usable = 0;

        foreach (var name in hosts)
        {
            if (opensAt(waiting[name].Peek()) <= now)
                usable++;
        }

        return usable < wanted;
    }

    /// <summary>The next item: from the host that opens soonest, or the one after the last when nothing says.</summary>
    /// <remarks>
    /// Something is always handed over. Holding the item back until a host opens would stop every worker at
    /// once, since they all pull from here, and turn one shut gate into a stopped run — so the worst case is
    /// that a caller waits at the gate that would have made it wait anyway.
    /// </remarks>
    private static T Next<T>(
        Dictionary<string, Queue<T>> waiting,
        List<string> hosts,
        Func<T, DateTimeOffset>? opensAt,
        ref int cursor)
    {
        var chosen = opensAt is null ? Round(hosts, cursor) : Soonest(waiting, hosts, opensAt, cursor);

        var name = hosts[chosen];

        cursor = chosen + 1;

        return waiting[name].Dequeue();
    }

    private static int Round(List<string> hosts, int cursor) => cursor >= hosts.Count ? 0 : cursor;

    /// <summary>The host in hand whose next item could be taken soonest, starting the round at the cursor.</summary>
    /// <remarks>
    /// Starting at the cursor is what keeps hosts that open at the same moment taking turns rather than one of
    /// them being served until it runs dry.
    /// </remarks>
    private static int Soonest<T>(
        Dictionary<string, Queue<T>> waiting,
        List<string> hosts,
        Func<T, DateTimeOffset> opensAt,
        int cursor)
    {
        var chosen = Round(hosts, cursor);
        var soonest = DateTimeOffset.MaxValue;

        for (var step = 0; step < hosts.Count; step++)
        {
            var index = (Round(hosts, cursor) + step) % hosts.Count;
            var opens = opensAt(waiting[hosts[index]].Peek());

            if (opens >= soonest)
                continue;

            soonest = opens;
            chosen = index;
        }

        return chosen;
    }

    /// <summary>Drops the hosts whose queues have run dry, so the count of what is in hand stays true.</summary>
    /// <remarks>
    /// Done before reading ahead rather than after choosing, because the decision to read more is made from
    /// that count: dropping afterwards left a drained host being counted as one in hand, and the round stopped
    /// growing while the source still had work for it.
    /// </remarks>
    private static void Emptied<T>(Dictionary<string, Queue<T>> waiting, List<string> hosts, ref int cursor)
    {
        for (var index = hosts.Count - 1; index >= 0; index--)
        {
            if (waiting[hosts[index]].Count > 0)
                continue;

            waiting.Remove(hosts[index]);
            hosts.RemoveAt(index);

            if (index < cursor)
                cursor--;
        }
    }
}
