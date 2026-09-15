namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Counts how many pages may be open on the browser at once, and remembers when the last one was handed back.
/// </summary>
/// <remarks>
/// The ceiling the pool enforces is a count of PAGES, not of browser processes, and this is where that count
/// lives. A caller waits here before it is given a browser and returns its permit when the lease is disposed;
/// the idle reaper comes here to take every permit at once, which is the only way it can know that nobody is
/// inside.
/// <para>
/// The clock belongs with the count rather than beside it because the two only ever move together: a permit
/// coming back IS the page closing, and that instant is the one the reaper measures idleness from. Kept apart
/// they would be two facts that have to be kept in step by whoever remembers to.
/// </para>
/// <para>
/// THIS TYPE IS DELIBERATELY NOT DISPOSABLE, and that is the point of it being a type. The semaphore inside
/// must outlive the pool that owns it: a lease returned after shutdown releases its permit by definition — a
/// crawl is cancelled while pages are still open — and a disposed semaphore answers that with an
/// <see cref="ObjectDisposedException"/> raised out of somebody's <c>await using</c>, which is the last thing
/// anyone reads while looking for why the run stopped. Nothing here holds a handle worth reclaiming:
/// <see cref="SemaphoreSlim"/> allocates one only for <c>AvailableWaitHandle</c>, which nobody asks for. That
/// used to be a paragraph in the pool that applied to one of its two semaphores and was not extended to the
/// other; a type that cannot be disposed cannot have the rule applied to half of it.
/// </para>
/// </remarks>
internal sealed class Permits(int ceiling)
{
    private readonly SemaphoreSlim _free = new(ceiling, ceiling);

    private long _idleSince = DateTimeOffset.UtcNow.UtcTicks;

    /// <summary>How many pages may be open at once.</summary>
    internal int Ceiling => ceiling;

    /// <summary>Waits for permission to open one page.</summary>
    internal Task WaitAsync(CancellationToken ct) => _free.WaitAsync(ct);

    /// <summary>Hands one page's permission back, and starts the idle clock from now.</summary>
    internal void Return()
    {
        Interlocked.Exchange(ref _idleSince, DateTimeOffset.UtcNow.UtcTicks);

        _free.Release();
    }

    /// <summary>Whether nothing has been handed back for longer than the given span.</summary>
    internal bool IsIdleFor(TimeSpan span)
        => DateTimeOffset.UtcNow.UtcTicks - Interlocked.Read(ref _idleSince) > span.Ticks;

    /// <summary>Takes every permit, or none, and says which.</summary>
    /// <remarks>
    /// All or nothing is what makes closing the browser safe without any cancellation juggling: a caller is
    /// either holding a permit, in which case this fails and the browser is left alone, or it is not, in which
    /// case it cannot be inside. A partial take would be worse than no take at all — it would block new
    /// callers while the ones already in flight carried on using the browser it was about to close.
    /// </remarks>
    internal bool Emptied()
    {
        var taken = 0;

        while (taken < ceiling && _free.Wait(0))
            taken++;

        if (taken == ceiling)
            return true;

        if (taken > 0)
            _free.Release(taken);

        return false;
    }

    /// <summary>Gives every permit back after an <see cref="Emptied"/> that succeeded.</summary>
    internal void Refilled() => _free.Release(ceiling);
}
