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
/// A failed item is a result, not an exception: one unreachable site must not cancel the whole batch. That
/// used to be a sentence in this paragraph and nothing else — the caller was trusted to catch everything, and
/// a caller that did not took the whole run down with one page. It is now a parameter, so there is no way to
/// fan work out without saying what a failed item looks like.
/// </remarks>
public static class Fanout
{
    /// <summary>Streams the results of <paramref name="work"/> over <paramref name="source"/>, as they finish.</summary>
    /// <remarks>
    /// An item that throws becomes the result <c>failed</c> gives for it, and the batch carries on.
    /// Results arrive in completion order, not source order. The channel is bounded to the degree of
    /// parallelism, so a slow consumer stops the producer instead of letting memory grow.
    /// <para>
    /// Cancellation is not a failure and is never translated: a cancelled run stops, it does not come back as
    /// a thousand cancelled items.
    /// </para>
    /// </remarks>
    public static IAsyncEnumerable<TResult> OverAsync<TSource, TResult>(
        IEnumerable<TSource> source,
        Func<TSource, CancellationToken, ValueTask<TResult>> work,
        Func<TSource, Exception, TResult> failed,
        int maxDegreeOfParallelism,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(failed);

        return Streamed(source, work, failed, Math.Max(1, maxDegreeOfParallelism), ct);
    }

    private static async IAsyncEnumerable<TResult> Streamed<TSource, TResult>(
        IEnumerable<TSource> source,
        Func<TSource, CancellationToken, ValueTask<TResult>> work,
        Func<TSource, Exception, TResult> failed,
        int degree,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<TResult>(degree);

        using var producing = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var producer = ProduceAsync(source, work, failed, channel.Writer, degree, producing.Token);

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
        Func<TSource, Exception, TResult> failed,
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
                    var result = await AttemptedAsync(item, work, failed, token);

                    await writer.WriteAsync(result, token);
                });

            writer.Complete();
        }
        catch (Exception exception)
        {
            writer.Complete(exception);
        }
    }

    /// <summary>One item's result, whether the work produced it or threw on the way.</summary>
    /// <remarks>
    /// Only a cancellation the caller asked for travels on. Anything else becomes an item, because the
    /// alternative is that a single page of a ten-thousand-page crawl ends the crawl — and ends it through
    /// <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource},ParallelOptions,Func{TSource,CancellationToken,ValueTask})"/>,
    /// which cancels the work already in flight, so the items that had succeeded are lost with it.
    /// </remarks>
    private static async ValueTask<TResult> AttemptedAsync<TSource, TResult>(
        TSource item,
        Func<TSource, CancellationToken, ValueTask<TResult>> work,
        Func<TSource, Exception, TResult> failed,
        CancellationToken ct)
    {
        try
        {
            return await work(item, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return failed(item, exception);
        }
    }
}
