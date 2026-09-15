using Snail.Toolkit.Web.Streaming;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Drives the shared fan-out with work that misbehaves rather than merely fails.
/// </summary>
/// <remarks>
/// <see cref="Fanout"/> already answers the failure it was written for — an item that throws is a result and
/// the batch carries on — and the suite covers that. What it has never been given is work that does not come
/// back when it is asked to, which is the ordinary condition of anything reaching over a network.
/// <para>
/// Neither caller that exists can produce it. The crawler is bounded by Playwright's navigation timeout and
/// the search by a Polly total-request timeout, so work that ignores its token is not something
/// <c>Crawler</c> or <c>WebSearch</c> can hand over. This was written while <see cref="Fanout"/> was public,
/// when the answer was "the work is a stranger's"; the type became internal on 2026-09-15 and that answer is
/// gone with it.
/// <para>
/// The check stays, and what it holds is the shape rather than a caller: <c>Fanout</c> is the place inside
/// this toolkit where bounded parallelism is reached for, a third caller costs nothing to add, and the
/// requirement that the work watch its token is written in prose nobody has to read. This is the guard that
/// makes leaving the loop unconditional, and it is the reason the bound in <c>Ended</c> can be justified at
/// all now that there is no consumer to justify it.
/// </para>
/// </remarks>
public class FanoutChaosTests
{
    /// <summary>Leaving the loop early gives the caller its thread back, whatever the work is doing.</summary>
    /// <remarks>
    /// <c>Streamed</c> cancels the producer and then awaits it, so the <c>await foreach</c> cannot finish
    /// disposing until the last item in flight has returned. Work that ignores its token therefore does not
    /// merely leak — it holds the consumer, which asked to stop, for as long as it likes.
    /// <para>
    /// The <c>finally</c> is right and the guarantee is worth having; what is missing is that the wait for it
    /// is unbounded. A <c>break</c> out of a loop is the one place a caller has said it no longer cares about
    /// the answer, and it is the one place this blocks on it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task OverAsync_ConsumerLeavingEarlyWhileWorkIgnoresTheToken_StillGivesTheCallerBack()
    {
        var leaving = Task.Run(async () =>
        {
            // The first item answers at once so that there is something to leave on; the rest hold their
            // worker. Had every item hung, the loop would never reach its own break and this would be
            // measuring how long a batch takes to produce nothing, which is a different sentence entirely.
            await foreach (var answer in Fanout.OverAsync(
                               Enumerable.Range(0, 8),
                               async (item, _) =>
                               {
                                   // Work that does not watch its token: a synchronous driver, a client whose
                                   // own timeout is longer, a native call. None of them is exotic.
                                   if (item > 0)
                                       await Task.Delay(TimeSpan.FromMinutes(1), CancellationToken.None);

                                   return item;
                               },
                               (item, _) => item,
                               4))
            {
                _ = answer;

                break;
            }
        });

        var finished = await Task.WhenAny(leaving, Task.Delay(TimeSpan.FromSeconds(15)));

        Assert.True(
            ReferenceEquals(finished, leaving),
            "the consumer broke out of the loop and is still held by work it stopped waiting for");
    }
}
