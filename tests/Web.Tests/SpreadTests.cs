using Snail.Toolkit.Web.Streaming;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the order that decides how many different sites are read at once.
/// </summary>
/// <remarks>
/// Throughput on a crawl is the number of different hosts in flight, not the number of workers: a host is held
/// to one request at a time, so workers pointed at the same host queue behind each other while other sites go
/// untouched. These checks are about the order alone — nothing here waits, paces or reads anything.
/// </remarks>
public class SpreadTests
{
    [Fact]
    public void OverHosts_ItemsGroupedByHost_ComeBackInTurn()
    {
        var spread = Spread.OverHosts(["a1", "a2", "a3", "b1", "b2", "b3"], HostOf, 4);

        Assert.Equal(["a1", "b1", "a2", "b2", "a3", "b3"], spread);
    }

    /// <summary>Nothing is lost and nothing is invented, whatever the order turns out to be.</summary>
    /// <remarks>
    /// The check every reordering needs and the one an ordering check cannot replace: a version that dropped
    /// the tail of a long host would pass the alternation check on its first few items.
    /// </remarks>
    [Fact]
    public void OverHosts_AnyMixture_LosesAndInventsNothing()
    {
        string[] items = ["a1", "b1", "c1", "a2", "a3", "a4", "b2", "d1", "a5", "c2"];

        var spread = Spread.OverHosts(items, HostOf, 4);

        Assert.Equal([.. items.Order(StringComparer.Ordinal)], [.. spread.Order(StringComparer.Ordinal)]);
    }

    /// <summary>One host's items keep the order they arrived in.</summary>
    /// <remarks>
    /// A crawl that put the newest pages first still means it, and a sitemap read newest-first would otherwise
    /// come back shuffled within each site — the one property a caller of this could not restore afterwards.
    /// </remarks>
    [Fact]
    public void OverHosts_ItemsOfOneHost_KeepTheirOrder()
    {
        var spread = Spread.OverHosts(["a1", "b1", "a2", "b2", "a3"], HostOf, 4);

        Assert.Equal(["a1", "a2", "a3"], spread.Where(item => item.StartsWith('a')));
    }

    /// <summary>A source of one host is handed back as it came, which is the case the memory bound exists for.</summary>
    [Fact]
    public void OverHosts_SourceOfOneHost_IsUnchanged()
    {
        string[] items = [.. Enumerable.Range(0, 500).Select(index => $"a{index}")];

        Assert.Equal(items, Spread.OverHosts(items, HostOf, 4));
    }

    /// <summary>It reads no further than it needs to, so an endless source is still streamed.</summary>
    /// <remarks>
    /// The property that makes this safe to put in front of a crawl: the frontier of a crawl is produced as it
    /// is consumed, and an order that drained its source first would turn a stream into a list and a bounded
    /// memory into an unbounded one.
    /// </remarks>
    [Fact]
    public void OverHosts_EndlessSource_IsNotDrained()
    {
        var produced = 0;

        var spread = Spread.OverHosts(Endless(), HostOf, 4).Take(10).ToArray();

        Assert.Equal(10, spread.Length);
        Assert.True(produced < 500, $"the order read {produced} items ahead to answer with ten");

        IEnumerable<string> Endless()
        {
            for (var index = 0; ; index++)
            {
                produced++;

                yield return $"{(char)('a' + index % 3)}{index}";
            }
        }
    }

    /// <summary>A host that is shut is stepped over while another has work.</summary>
    /// <remarks>
    /// The case that stops a run without anything being wrong with it: workers handed sites that have just
    /// asked to be left alone sit out the rest together while other sites go unread.
    /// </remarks>
    [Fact]
    public void OverHosts_HostThatIsShut_IsSteppedOver()
    {
        var spread = Spread.OverHosts(
            ["a1", "a2", "b1", "b2"],
            HostOf,
            4,
            opensAt: item => item.StartsWith('a') ? Later : Now);

        Assert.Equal(["b1", "b2", "a1", "a2"], spread);
    }

    /// <summary>Of two shut hosts the one that opens sooner is chosen.</summary>
    /// <remarks>
    /// The reason the answer is an instant and not a yes or no. A host busy for its own twenty-millisecond gap
    /// and a host resting five seconds are both "not open", and a chooser told only that picks between them at
    /// random — which is to say it picks the five seconds a third of the time. Measured through the crawl on
    /// 2026-09-14: with three sites resting and a yes-or-no answer, seven of eighteen open pages were read in
    /// the window where all eighteen should have been.
    /// </remarks>
    [Fact]
    public void OverHosts_TwoShutHosts_TakesTheOneThatOpensSooner()
    {
        var spread = Spread.OverHosts(
            ["a1", "b1"],
            HostOf,
            4,
            opensAt: item => item.StartsWith('a') ? Later : Soon);

        Assert.Equal(["b1", "a1"], spread);
    }

    /// <summary>When nothing is open the work is handed over anyway.</summary>
    /// <remarks>
    /// Holding it back would stop every worker at once, since they all pull from the same order, and turn one
    /// shut gate into a stopped run. The caller waits at that host's gate instead, exactly as it did before
    /// any of this existed.
    /// </remarks>
    [Fact]
    public void OverHosts_NothingOpen_HandsTheWorkOverAnyway()
    {
        var spread = Spread.OverHosts(["a1", "b1", "a2"], HostOf, 4, opensAt: _ => Later);

        Assert.Equal(["a1", "b1", "a2"], spread);
    }

    private static DateTimeOffset Now => DateTimeOffset.MinValue;

    /// <summary>A source of one host is held back by a bound rather than drained into memory.</summary>
    /// <remarks>
    /// ONE HOST IS THE CASE THAT REACHES THE BOUND, and no other does — which is why this check looks nothing
    /// like the one above it. Read-ahead normally stops because <c>wanted</c> different hosts are in hand; a
    /// source that offers only one host can never satisfy that, so it runs until
    /// <c>held &lt; wanted * HeldBackPerHost</c> stops it. That is the only line standing between a crawl of
    /// one site and its whole frontier held in memory at once.
    /// <para>
    /// MEASURED on 2026-09-15, two hundred thousand addresses of one host, ten taken:
    /// <list type="bullet">
    /// <item><description>with the bound — 13 KB.</description></item>
    /// <item><description>without it — 20.8 GB, and the check took thirty-two seconds to get there.</description></item>
    /// </list>
    /// The ceiling of one megabyte is derived from both: eighty times the observed cost, and twenty thousand
    /// times below the failure. Nothing this check could drift into lands between them.
    /// </para>
    /// <para>
    /// PER THREAD, NOT PER PROCESS, and that is not a detail. <see cref="GC.GetTotalAllocatedBytes"/> counts
    /// the whole process, and xunit runs test classes in parallel — the first version of this measurement
    /// reported a refusal at depth twenty thousand costing twenty-five megabytes and the same refusal at five
    /// thousand costing under one, for work that is identical by construction. That was other tests
    /// allocating on other threads. Everything measured here is synchronous, so the per-thread counter is
    /// exact; an asynchronous one would need the process-wide counter and a threshold loose enough to survive
    /// the noise.
    /// </para>
    /// </remarks>
    [Fact]
    public void OverHosts_ASourceOfOneHost_HoldsBackABoundedNumberOfItems()
    {
        string[] items = [.. Enumerable.Range(0, 200_000).Select(index => $"a{index}")];

        var before = GC.GetAllocatedBytesForCurrentThread();

        var spread = Spread.OverHosts(items, HostOf, 4).Take(10).ToArray();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(10, spread.Length);
        Assert.True(
            allocated < 1024L * 1024,
            $"ten addresses of one host cost {allocated / 1024} KB, which is the source being drained");
    }

    private static DateTimeOffset Soon => DateTimeOffset.UtcNow.AddMilliseconds(20);

    private static DateTimeOffset Later => DateTimeOffset.UtcNow.AddSeconds(5);

    /// <summary>The host of an item is its first letter, which keeps these checks about the order.</summary>
    private static string HostOf(string item) => item[..1];
}
