using Snail.Toolkit.Web.Streaming;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the streaming primitive both engines fan work out through.
/// </summary>
public class FanoutTests
{
    [Fact]
    public async Task OverAsync_EveryItem_ComesBackExactlyOnce()
    {
        var seen = new List<int>();

        await foreach (var result in Fanout.OverAsync(Enumerable.Range(0, 50), Doubled, 4))
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

        await foreach (var _ in Fanout.OverAsync(Enumerable.Range(0, 1_000), Counted, 1))
            break;

        Assert.True(started < 1_000, $"the producer kept going and started {started} of 1000 items");

        ValueTask<int> Counted(int number, CancellationToken ct)
        {
            Interlocked.Increment(ref started);

            return ValueTask.FromResult(number);
        }
    }

    [Fact]
    public async Task OverAsync_WorkThatThrows_FaultsTheEnumeration()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var _ in Fanout.OverAsync(Enumerable.Range(0, 10), Exploding, 2))
            {
            }
        });

        Assert.Contains("boom", $"{error} {error.InnerException}", StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverAsync_CancelledCaller_StopsWithCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Fanout.OverAsync(Enumerable.Range(0, 10), Doubled, 2, cancelled.Token))
            {
            }
        });
    }

    /// <summary>Arguments are checked when the call is made, not when enumeration first moves.</summary>
    [Fact]
    public void OverAsync_WithoutASource_ThrowsBeforeEnumerationBegins()
        => Assert.Throws<ArgumentNullException>(() => Fanout.OverAsync<int, int>(null!, Doubled, 2));

    private static ValueTask<int> Doubled(int number, CancellationToken ct) => ValueTask.FromResult(number * 2);

    private static ValueTask<int> Exploding(int number, CancellationToken ct)
        => throw new InvalidOperationException("boom");
}
