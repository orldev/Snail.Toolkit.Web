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
internal static class Fanout
{
    /// <summary>How long a consumer that has left waits for work it has already cancelled.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

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

        return StreamedAsync(source, work, failed, Math.Max(1, maxDegreeOfParallelism), ct);
    }

    private static async IAsyncEnumerable<TResult> StreamedAsync<TSource, TResult>(
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

            await EndedAsync(producer);
        }
    }

    /// <summary>Waits for the producer to finish, and stops waiting if it will not.</summary>
    /// <remarks>
    /// Tying the producer to the enumeration is what keeps it from blocking forever on a channel nobody reads,
    /// and awaiting it here is how that tie is honoured — a consumer that leaves early should not walk away
    /// from work still holding a lease. What it must not do is make leaving conditional on the work agreeing
    /// to stop. Cancellation has already been asked for; work that does not watch its token would otherwise
    /// hold the caller for as long as it likes, and the caller has said it no longer wants the answer.
    /// <para>
    /// The grace period is long enough that an orderly stop is always awaited and short enough that a
    /// disorderly one is not mistaken for progress; the work is then left to finish on its own, where its
    /// exception is observed rather than raised at a caller that has already gone.
    /// </para>
    /// <para>
    /// WHOSE CARELESSNESS THIS GUARDS AGAINST CHANGED ON 2026-09-15, and the paragraph is rewritten rather
    /// than left standing because it had become untrue. This bound was added while <see cref="Fanout"/> was
    /// public, and the reason given was that the work would be a stranger's; the type is internal now, so
    /// there is no stranger.
    /// </para>
    /// <para>
    /// That paragraph went on to say that neither caller that exists could produce the failure this prevents,
    /// and on 2026-09-20 it was MEASURED FALSE: the search handed its attempts a token of its own and threw
    /// away the one given here, so a caller that broke out of the loop waited the full grace while a slow
    /// engine ran on. The search now listens to this token as well. The bound is what caught it — the caller
    /// waited five seconds rather than for as long as the engine liked.
    /// </para>
    /// <para>
    /// It is kept, and the reason is no longer a supposition: what it guards against is OUR OWN caller, and
    /// one of ours has already been that caller. Nothing about this type makes a third one expensive to add, and
    /// nothing about it warns that the work handed over must watch its token — the requirement lives here, in
    /// prose, where an author reaching for bounded parallelism will not look. The cost of keeping it is a
    /// timer that elapses only when a caller has made that mistake. That is a smaller price than the failure, which is silent: a consumer that
    /// has already left the loop, still held.
    /// </para>
    /// </remarks>
    private static async Task EndedAsync(Task producer)
    {
        try
        {
            await producer.WaitAsync(Grace);
        }
        catch (TimeoutException)
        {
            _ = producer.ContinueWith(
                ended => ended.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
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
