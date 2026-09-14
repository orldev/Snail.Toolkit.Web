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
/// This is public API in the core package, so the work is whatever a consumer passes. Inside the toolkit both
/// callers happen to be bounded — Playwright's own navigation timeout on one side, a Polly total-request
/// timeout on the other — so nothing here is reachable from <c>Crawler</c> or <c>WebSearch</c> today. It is
/// reachable from the first consumer who fans out over something else, and the shape is the same one that
/// made <c>Fanout</c> tie its producer to the enumeration in the first place.
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
