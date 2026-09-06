using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Snail.Toolkit.Web.Streaming;

/// <summary>
/// Runs bounded work over a source and streams each result the moment it is ready.
/// </summary>
/// <remarks>
/// The producer is tied to the lifetime of the enumeration by a linked token and a <c>finally</c>. Without
/// them the <c>await</c> on the producer sits after the loop and is unreachable whenever the consumer leaves
/// early — on a <c>break</c>, an exception or a cancellation — and the producer blocks forever writing to a
/// bounded channel with no reader, holding whatever resource it had leased.
/// A failed item is a result, not an exception: one unreachable site must not cancel the whole batch, so the
/// result type is expected to carry failure as data.
/// </remarks>
public static class Fanout
{
    /// <summary>Streams the results of <paramref name="work"/> over <paramref name="source"/>, as they finish.</summary>
    /// <remarks>
    /// Results arrive in completion order, not source order. The channel is bounded to the degree of
    /// parallelism, so a slow consumer stops the producer instead of letting memory grow.
    /// </remarks>
    public static IAsyncEnumerable<TResult> OverAsync<TSource, TResult>(
        IEnumerable<TSource> source,
        Func<TSource, CancellationToken, ValueTask<TResult>> work,
        int maxDegreeOfParallelism,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(work);

        return Streamed(source, work, Math.Max(1, maxDegreeOfParallelism), ct);
    }

    private static async IAsyncEnumerable<TResult> Streamed<TSource, TResult>(
        IEnumerable<TSource> source,
        Func<TSource, CancellationToken, ValueTask<TResult>> work,
        int degree,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<TResult>(degree);

        using var producing = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var producer = ProduceAsync(source, work, channel.Writer, degree, producing.Token);

        try
        {
            await foreach (var result in channel.Reader.ReadAllAsync(ct))
                yield return result;
        }
        finally
        {
            await producing.CancelAsync();
            await producer;
        }
    }

    private static async Task ProduceAsync<TSource, TResult>(
        IEnumerable<TSource> source,
        Func<TSource, CancellationToken, ValueTask<TResult>> work,
        ChannelWriter<TResult> writer,
        int degree,
        CancellationToken ct)
    {
        try
        {
            await Parallel.ForEachAsync(
                source,
                new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = ct },
                async (item, token) =>
                {
                    var result = await work(item, token);

                    await writer.WriteAsync(result, token);
                });

            writer.Complete();
        }
        catch (Exception exception)
        {
            writer.Complete(exception);
        }
    }
}
