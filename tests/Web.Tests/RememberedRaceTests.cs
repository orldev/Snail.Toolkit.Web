using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Snail.Toolkit.Web.Politeness;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Drives the forgetting that <see cref="Politeness.HostPoliteness"/> and <c>SiteRules</c> share, from the
/// one angle its own design note does not consider: what happens to a caller already holding what was forgotten.
/// </summary>
/// <remarks>
/// <c>Remembered&lt;T&gt;</c> hands a value out and then, on somebody else's arrival, decides that value is no
/// longer worth keeping. Between those two moments the first caller is still going to write to it — that is
/// the whole reason it asked — and the write lands on an object the map no longer holds.
/// <para>
/// This is the defect the gate was already cured of once, arriving through a different door. The cure on
/// 2026-09-14 made <c>Rest</c> a compare-and-exchange so that a long rest could not be clobbered by a short
/// one; a rest written to an evicted gate is not clobbered, it is simply never read, and the host stands open
/// exactly as it did before.
/// </para>
/// </remarks>
public class RememberedRaceTests
{
    private const int Callers = 64;

    private const int Rounds = 50;

    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(200);


    /// <summary>What <c>Of</c> handed over is still what the map holds when the caller writes to it.</summary>
    /// <remarks>
    /// Deterministic, and deliberately so: the interleaving this needs is not rare, it is the ordinary shape
    /// of every caller. <c>Of</c> returns a value that carries no decision yet — a gate holding nobody, an
    /// entry whose read has not started — which is precisely what the staleness predicate is looking for, so
    /// the next arrival of an unknown key sweeps it away before its owner has had a chance to make it worth
    /// keeping.
    /// <para>
    /// Sweeping on the arrival of an unknown key is the right moment; the map is only ever growing then. What
    /// is missing is that a value handed out a moment ago cannot yet be stale, whatever the predicate says
    /// about the state it has not been given.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Of_ValueHandedOutAndThenWrittenTo_IsStillTheValueTheMapHolds()
    {
        // A real forgetting window rather than zero. Zero was the first draft and it asserts the impossible:
        // "forget immediately" is what it asks for, so nothing can promise the entry survives. The window has
        // to be short enough that the sweep throttle — a quarter of it — lets the second arrival sweep.
        var remembered = new Remembered<Gate>(Window, (gate, since) => gate.RestingUntil <= since, TimeProvider.System);

        var mine = remembered.Of("a.example", _ => new Gate());

        await Task.Delay(Window / 2);

        // Another caller arrives with a host nobody has mentioned. That is the only moment anything is swept,
        // and what it swept was the gate above: nothing is written to it yet, so it looked disposable.
        remembered.Of("b.example", _ => new Gate());

        // Only now does the first caller write down what it learned — the site asked to be left alone.
        mine.RestingUntil = DateTimeOffset.UtcNow + Long;

        Assert.Same(mine, remembered.Of("a.example", _ => new Gate()));
    }

    /// <summary>
    /// The same window on an entry that is genuinely old: the residency rule cannot help there, and the caller
    /// holding the value is in exactly the position the rule was written to protect.
    /// </summary>
    /// <remarks>
    /// Read on 2026-09-20. The cure of 2026-09-15 keeps an entry for its first window, which closes the case
    /// where the value was made a moment ago. It says nothing about a host visited an hour ago and visited
    /// again now: that entry is older than any window, its gate is idle because nothing has happened on it
    /// since, and the caller that has just been handed it is one <c>await</c> away from entering it. A sweep in
    /// that moment drops the gate the caller holds — so its rest is written where nobody will read it, and the
    /// next caller builds a second gate for the same host.
    /// <para>
    /// Deterministic: the clock is moved by hand, so "old" is a fact rather than a wait, and the sweep is
    /// triggered by the arrival of a host nobody has mentioned, which is the only moment anything is swept.
    /// </para>
    /// <para>
    /// The cure is to measure the window from the last time the entry was ASKED FOR rather than from when it
    /// was made. What a per-host window means is "nobody has been to this host lately", and being handed to a
    /// caller is exactly the evidence that somebody has.
    /// </para>
    /// </remarks>
    [Fact]
    public void Of_AgedValueHandedOutAndThenWrittenTo_IsStillTheValueTheMapHolds()
    {
        var clock = new FakeTimeProvider();
        var remembered = new Remembered<Gate>(Window, (gate, since) => gate.RestingUntil <= since, clock);

        remembered.Of("a.example", _ => new Gate());

        // Nobody goes near that host for far longer than the forgetting window, and then somebody does.
        clock.Advance(Window * 4);

        var mine = remembered.Of("a.example", _ => new Gate());

        // Another caller arrives with a host nobody has mentioned, which is what sweeps.
        remembered.Of("b.example", _ => new Gate());

        // Only now does the caller write down what it learned — the site asked to be left alone.
        mine.RestingUntil = clock.GetUtcNow() + Long;

        Assert.Same(mine, remembered.Of("a.example", _ => new Gate()));
    }

    /// <summary>A host held to one request at a time is still held to one when the map forgets it.</summary>    /// <summary>A host held to one request at a time is still held to one when the map forgets it.</summary>
    /// <remarks>
    /// The other consequence of the same window, and the more expensive one. <c>EnterAsync</c> takes the gate
    /// and then waits on its semaphore; a sweep in between drops that gate, so the next caller for the same
    /// host builds a second one — with a second semaphore, and no knowledge of the first. Two callers are
    /// then inside one host at once, each correctly holding a slot, of different gates.
    /// <para>
    /// <c>MaxConcurrentPerHost</c> is the setting that is meant to make this impossible, and the failure is
    /// silent: nothing throws, nothing is logged, and the only evidence is at the site being asked twice.
    /// </para>
    /// <para>
    /// A load guard rather than the check that discriminates the cure — that one is above, and deterministic.
    /// Once an entry is kept for its first window no caller can be handed a gate the map has dropped at any
    /// sane setting, so what this measures is that the ordinary path stays correct under sixty-four callers,
    /// which is the property that would quietly stop being true if the residency rule were ever relaxed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EnterAsync_HostForgottenBetweenLookupAndTurn_StillAdmitsOneCallerAtATime()
    {
        // A forgetting window that is short enough to keep sweeping throughout and long enough to outlast
        // the scheduler. One millisecond was tried first and is not a setting, it is a coin toss: a caller
        // descheduled between finding its gate and reaching its slot is away for longer than that under any
        // load, so the run went red about one time in three with nothing wrong.
        using var politeness = Forgetful(Window);

        // One counter PER HOST. A single shared counter measures nothing: callers on different rounds are on
        // different hosts and hold turns at the same time entirely correctly, which is the whole point of
        // pacing per host — counted together they read as thousands of violations that never happened.
        var inside = new int[Rounds];
        var together = 0;

        await Task.WhenAll(Enumerable.Range(0, Callers).Select(caller => Task.Run(async () =>
        {
            for (var round = 0; round < Rounds; round++)
            {
                // Every caller mentions a host of its own first, so that a sweep is always in progress.
                await using (await politeness.EnterAsync(Host($"filler-{caller}", round)))
                {
                }

                await using (await politeness.EnterAsync(Host("contested", round)))
                {
                    if (Interlocked.Increment(ref inside[round]) > 1)
                        Interlocked.Increment(ref together);

                    await Task.Delay(1);

                    Interlocked.Decrement(ref inside[round]);
                }
            }
        })));

        Assert.Equal(0, together);
    }

    private static Uri Host(string label, int round) => new($"https://{label}-{round}.example/a");

    private static HostPoliteness Forgetful(TimeSpan forgetAfter)
        => new(Options.Create(new PolitenessOptions
        {
            MinDelayPerHost = TimeSpan.Zero,
            MaxConcurrentPerHost = 1,
            ForgetHostAfter = forgetAfter
        }));

    /// <summary>Stands in for what a map like this remembers: something written to after it is handed over.</summary>
    private sealed class Gate
    {
        public DateTimeOffset RestingUntil { get; set; } = DateTimeOffset.MinValue;
    }
}
