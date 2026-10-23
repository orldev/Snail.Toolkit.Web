using Microsoft.Extensions.Logging;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Closes the browser when nothing has used it for a while, and never while anybody is inside.
/// </summary>
/// <remarks>
/// It reaches the pool through two questions and nothing else — whether a browser is open, and please close
/// it — which is the whole of the coupling and the reason this could be lifted out while the opening could
/// not. Everything it needs to decide WHETHER to close is in <see cref="Permits"/>: the idle clock and the
/// all-or-nothing take that proves nobody is holding a page.
/// <para>
/// A pool connected to a browser server never runs one of these. The browser on the other end was launched by
/// somebody else and outlives every connection to it, so closing "the idle browser" would end our connection
/// and reap nothing — MEASURED on 2026-09-14 by <c>probes/contexts</c>: the process count in that container
/// stays at one however many connections come and go.
/// </para>
/// </remarks>
internal sealed class Reaper(
    TimeSpan idleTimeout,
    TimeProvider clock,
    Permits permits,
    Func<bool> isOpen,
    Func<Task> closeAsync,
    ILogger logger)
{
    /// <summary>Sweeps until the token is cancelled, or returns at once when there is nothing to sweep.</summary>
    /// <remarks>
    /// An idle timeout of zero or less switches the reaper off rather than making it sweep constantly, which
    /// is what a period of zero would otherwise mean to <see cref="PeriodicTimer"/>.
    /// </remarks>
    internal async Task ReapAsync(CancellationToken ct)
    {
        if (idleTimeout <= TimeSpan.Zero)
            return;

        using var timer = new PeriodicTimer(Period(idleTimeout), clock);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SweepOnceAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>One look at the browser: close it if it is open, idle, and nobody is holding a page.</summary>
    /// <remarks>
    /// The three questions are asked in that order because each is cheaper than the next and each can rule the
    /// others out. Taking every permit is the expensive one and the one with a consequence — it blocks new
    /// callers until it is given back — so it is asked last and only once the answer could matter.
    /// <para>
    /// The <c>finally</c> is load-bearing rather than tidy: a close that throws would otherwise leave every
    /// permit taken and the pool unable to hand out a single page again, which is a worse outcome than the
    /// browser that would not close.
    /// </para>
    /// </remarks>
    private async Task SweepOnceAsync()
    {
        if (!isOpen())
            return;

        if (!permits.IsIdleFor(idleTimeout))
            return;

        if (!permits.TryEmpty())
            return;

        try
        {
            Log.IdleClosing(logger, idleTimeout);

            await closeAsync();
        }
        catch (Exception exception)
        {
            Log.IdleCloseFailed(logger, exception);
        }
        finally
        {
            permits.Refilled();
        }
    }

    /// <summary>How often to look, which is twice per idle window and never faster than once a second.</summary>
    /// <remarks>
    /// Half the window bounds how long a browser outlives its own timeout; the floor keeps a short timeout
    /// from turning the sweep into a spin.
    /// </remarks>
    private static TimeSpan Period(TimeSpan idleTimeout)
    {
        var half = idleTimeout / 2;

        return half < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : half;
    }
}
