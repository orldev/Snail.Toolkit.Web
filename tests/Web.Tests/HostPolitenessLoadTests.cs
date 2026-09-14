using System.Diagnostics;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Politeness;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Drives the gate from many callers at once, which is the only way its state is ever shared.
/// </summary>
/// <remarks>
/// The gate is a singleton holding mutable state, and until this file existed nothing in the suite ever
/// entered it from two threads: every check in <see cref="HostPolitenessTests"/> is sequential, and the
/// browser path reduces to one worker at the defaults because a lease is held for the whole visit. A
/// defect reachable only under load was therefore not merely under-covered — no test could execute it.
/// <para>
/// The callers are threads rather than tasks, and they are released by a barrier. Measured on 2026-09-14:
/// the same rounds driven by <c>Task.Run</c> found nothing at all, because the pool injects threads slowly
/// and a hundred queued callers run all but sequentially — the harness has to make the callers simultaneous
/// before it can measure what happens when they are.
/// </para>
/// <para>
/// Failure here is by nature probabilistic: a lost update needs one caller to read the field between
/// another's read and its write. That is what the rounds are for — one round proves nothing, and a hundred
/// of them turn a rare interleaving into a near-certain one. Rounds are cheap because the gate is only
/// asked whether it is open, never waited on.
/// </para>
/// </remarks>
public class HostPolitenessLoadTests
{
    private const int Callers = 64;

    private const int Rounds = 500;

    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(1);

    /// <summary>A long rest asked for at the same moment as short ones survives all of them.</summary>
    /// <remarks>
    /// <c>Rest</c> compares and then assigns, so a caller carrying a short gap can read the field before a
    /// longer rest is written and store its own afterwards. The site said thirty seconds and the gate ends
    /// up open, which reads as the site being hit again the instant after it asked to be left alone.
    /// <para>
    /// ONE long caller against sixty-three short ones, and the count is the measurement, not a preference.
    /// Half and half was tried first and stayed green through five hundred rounds even with the window
    /// widened by hand: a clobbered rest was written back by the next long caller microseconds later, so the
    /// defect happened constantly and was never observable at the end of a round. With a single long caller
    /// nothing repairs it, and the same rounds name a broken one on every run.
    /// </para>
    /// </remarks>
    [Fact]
    public void Rest_ALongGapRacingShortOnes_IsNotLost()
    {
        using var politeness = Gate(TimeSpan.Zero, 1);

        var open = RoundsThatBroke(
            (round, caller) => politeness.Rest(Host("lost", round), caller == 0 ? Long : Short),
            round => politeness.EnterAsync(Host("lost", round)).IsCompleted);

        Assert.Empty(open);
    }

    /// <summary>Short rests asked for at once leave the host open, not held for something nobody asked for.</summary>
    /// <remarks>
    /// The other half of the same field: <see cref="DateTimeOffset"/> is twelve bytes and is written outside
    /// any lock, so a reader can in principle see one caller's date beside another's offset. The consequence
    /// is not a lost rest but an invented one — a host held for as long as the torn value says.
    /// </remarks>
    [Fact]
    public void Rest_ManyShortGapsAtOnce_LeavesTheHostOpen()
    {
        using var politeness = Gate(TimeSpan.Zero, 1);

        var shut = RoundsThatBroke(
            (round, _) => politeness.Rest(Host("open", round), Short),
            round =>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(5));

                return !politeness.EnterAsync(Host("open", round)).IsCompleted;
            });

        Assert.Empty(shut);
    }

    /// <summary>A crowd on one host is still served one at a time, no faster than the configured gap.</summary>
    /// <remarks>
    /// The gap is measured from <c>ReleasedAt</c>, written by every turn as it ends and read by the next one,
    /// which is the second piece of state shared here. Fifty turns of twenty milliseconds is a second of
    /// runtime and the shortest crowd that makes the arithmetic unambiguous.
    /// </remarks>
    [Fact]
    public async Task EnterAsync_FiftyCallersOnOneHost_KeepsTheGapBetweenEveryTurn()
    {
        var gap = TimeSpan.FromMilliseconds(20);

        using var politeness = Gate(gap, 1);

        var host = new Uri("https://crowded.example/a");
        var started = Stopwatch.GetTimestamp();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(async _ =>
        {
            await using (await politeness.EnterAsync(host))
            {
            }
        }));

        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.True(elapsed >= gap * 45, $"fifty turns took {elapsed}, less than the gap allows");
    }

    /// <summary>Shutting the gate down under a turn still lets that turn finish.</summary>
    /// <remarks>
    /// <c>Dispose</c> disposes every semaphore, and a turn that has not ended yet releases one afterwards. A
    /// crawl cancelled while pages are in flight is the ordinary way to arrive here, and the disposal it
    /// answers with is thrown from an <c>await using</c>, which is where it hides the real reason the run
    /// stopped.
    /// </remarks>
    [Fact]
    public async Task Dispose_WhileATurnIsStillHeld_LetsThatTurnEnd()
    {
        var politeness = Gate(TimeSpan.Zero, 1);

        var turn = await politeness.EnterAsync(new Uri("https://example.com/a"));

        politeness.Dispose();

        await turn.DisposeAsync();
    }

    private static Uri Host(string label, int round) => new($"https://{label}-{round}.example/refused");

    private static HostPoliteness Gate(TimeSpan minDelay, int maxConcurrent)
        => new(Options.Create(new PolitenessOptions
        {
            MinDelayPerHost = minDelay,
            MaxConcurrentPerHost = maxConcurrent
        }));

    /// <summary>Runs the work on <see cref="Callers"/> simultaneous threads and names the rounds that broke.</summary>
    /// <remarks>
    /// Nothing is asserted while the threads are alive: an assertion thrown between rounds would dispose the
    /// barrier under callers still waiting on it, and a background thread failing on that takes the whole run
    /// down instead of the one test.
    /// </remarks>
    private static List<int> RoundsThatBroke(Action<int, int> work, Func<int, bool> isBroken)
    {
        using var barrier = new Barrier(Callers + 1);

        var broken = new List<int>();

        var threads = Enumerable.Range(0, Callers).Select(caller =>
        {
            var thread = new Thread(() =>
            {
                for (var round = 0; round < Rounds; round++)
                {
                    barrier.SignalAndWait();
                    work(round, caller);
                    barrier.SignalAndWait();
                }
            }) { IsBackground = true };

            thread.Start();

            return thread;
        }).ToArray();

        for (var round = 0; round < Rounds; round++)
        {
            barrier.SignalAndWait();
            barrier.SignalAndWait();

            if (isBroken(round))
                broken.Add(round);
        }

        foreach (var thread in threads)
            thread.Join();

        return broken;
    }
}
