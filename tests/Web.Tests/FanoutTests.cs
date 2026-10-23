using Snail.Toolkit.Web.Streaming;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the streaming primitive both engines fan work out through.
/// </summary>
public class FanoutTests
{
    /// <summary>The number a broken item comes back as, chosen so that no good item can produce it.</summary>
    private const int Broken = -1;

    [Fact]
    public async Task OverAsync_EveryItem_ComesBackExactlyOnce()
    {
        var seen = new List<int>();

        await foreach (var result in Fanout.OverAsync(Enumerable.Range(0, 50), Doubled, Failed, 4))
            seen.Add(result);

        Assert.Equal([.. Enumerable.Range(0, 50).Select(number => number * 2)], [.. seen.Order()]);
    }

    /// <summary>
    /// The producer used to be awaited after the loop, where a <c>break</c> never reaches it: it then blocked
    /// forever writing to a bounded channel nobody was reading, holding whatever it had leased.
    /// </summary>
    [Fact]
    public async Task OverAsync_ConsumerLeavingEarly_StopsTheProducer()
    {
        var started = 0;

        await foreach (var _ in Fanout.OverAsync(Enumerable.Range(0, 1_000), Counted, Failed, 1))
            break;

        Assert.True(started < 1_000, $"the producer kept going and started {started} of 1000 items");

        ValueTask<int> Counted(int number, CancellationToken ct)
        {
            Interlocked.Increment(ref started);

            return ValueTask.FromResult(number);
        }
    }

    /// <summary>An item that throws arrives as a result of its own.</summary>
    /// <remarks>
    /// It used to fault the enumeration, which this file asserted and the type's own documentation contradicted
    /// in the same breath — "one unreachable site must not cancel the whole batch" was a sentence, not a
    /// promise anything kept. Saying what a failed item looks like is now part of asking for the work.
    /// </remarks>
    [Fact]
    public async Task OverAsync_ItemThatThrows_ComesBackAsAResult()
    {
        var seen = new List<int>();

        await foreach (var result in Fanout.OverAsync(Enumerable.Range(0, 10), ExplodingOnFive, Failed, 2))
            seen.Add(result);

        Assert.Single(seen, Broken);
    }

    /// <summary>And the other nine items still arrive, which is the whole of the promise.</summary>
    /// <remarks>
    /// The check above passes on an implementation that answers one broken item and drops the rest.
    /// <c>Parallel.ForEachAsync</c> cancels the work in flight when one body throws, so the items lost to a
    /// single bad page were never only the bad one.
    /// </remarks>
    [Fact]
    public async Task OverAsync_ItemThatThrows_LeavesTheRestOfTheBatchAlone()
    {
        var seen = new List<int>();

        await foreach (var result in Fanout.OverAsync(Enumerable.Range(0, 10), ExplodingOnFive, Failed, 2))
            seen.Add(result);

        Assert.Equal(10, seen.Count);
        Assert.Equal([.. Enumerable.Range(0, 10).Where(number => number != 5).Select(number => number * 2)],
            [.. seen.Where(result => result != Broken).Order()]);
    }

    /// <summary>Cancellation is not a failure and is never translated into one.</summary>
    /// <remarks>
    /// The control that keeps the translation honest: without it, a cancelled run of ten thousand addresses
    /// would come back as ten thousand failed items and read as a catastrophe rather than as a stop.
    /// </remarks>
    [Fact]
    public async Task OverAsync_CancelledCaller_StopsWithCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Fanout.OverAsync(Enumerable.Range(0, 10), Doubled, Failed, 2, cancelled.Token))
            {
            }
        });
    }

    /// <summary>Work with two things to say about an item says the first before the second is ready.</summary>
    /// <remarks>
    /// The reason the handing overload exists: the search's second engine in a failed place, whose failure is
    /// news before the second engine answers. Handing the two over together would delay exactly that, and here
    /// it would also never finish — the second result waits for the first to be read — so the wait is capped,
    /// and a delay reads as a failure rather than a hung suite.
    /// </remarks>
    [Fact]
    public async Task OverAsync_WorkHandingOverTwice_DeliversTheFirstBeforeTheSecondIsReady()
    {
        var firstRead = new TaskCompletionSource();
        var seen = new List<int>();

        await foreach (var result in Fanout.OverAsync<int, int>([1], HandingTwice, Failed, 1))
        {
            seen.Add(result);
            firstRead.TrySetResult();
        }

        Assert.Equal([1, 2], seen);

        async ValueTask HandingTwice(int number, Func<int, ValueTask> hand, CancellationToken ct)
        {
            await hand(number);
            await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            await hand(number * 2);
        }
    }

    /// <summary>Work that throws after handing something over keeps what it handed over, and adds its failure.</summary>
    [Fact]
    public async Task OverAsync_WorkThrowingAfterHandingOver_KeepsWhatItHandedAndAddsTheFailure()
    {
        var seen = new List<int>();

        await foreach (var result in Fanout.OverAsync<int, int>([3], HandingThenThrowing, Failed, 1))
            seen.Add(result);

        Assert.Equal([3, Broken], seen);

        static async ValueTask HandingThenThrowing(int number, Func<int, ValueTask> hand, CancellationToken ct)
        {
            await hand(number);

            throw new InvalidOperationException("boom");
        }
    }

    /// <summary>Arguments are checked when the call is made, not when enumeration first moves.</summary>
    [Fact]
    public void OverAsync_WithoutASource_ThrowsBeforeEnumerationBegins()
        => Assert.Throws<ArgumentNullException>(() => Fanout.OverAsync<int, int>(null!, Doubled, Failed, 2));

    [Fact]
    public void OverAsync_WithoutAWayToFail_ThrowsBeforeEnumerationBegins()
        => Assert.Throws<ArgumentNullException>(() => Fanout.OverAsync(Enumerable.Range(0, 1), Doubled, null!, 2));

    private static ValueTask<int> Doubled(int number, CancellationToken ct) => ValueTask.FromResult(number * 2);

    private static ValueTask<int> ExplodingOnFive(int number, CancellationToken ct)
        => number is 5 ? throw new InvalidOperationException("boom") : ValueTask.FromResult(number * 2);

    private static int Failed(int number, Exception cause) => Broken;
}
