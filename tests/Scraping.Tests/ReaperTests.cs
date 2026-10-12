using Microsoft.Extensions.Logging.Abstractions;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers the idle reaper, which until it became a type of its own could not be covered at all.
/// </summary>
/// <remarks>
/// This branch was reported as untestable on 2026-09-14 and the report was honest: the reaper only ever ran
/// against a browser the pool had LAUNCHED, and the whole suite drives a browser SERVER, where the pool makes
/// no reaper at all. Reaching it needed a local browser, which needs a driver, which needs an installation the
/// suite does not have.
/// <para>
/// What made it reachable was not a new fixture but the seam: the reaper asks the pool two questions —
/// whether a browser is open, and please close it — and asks <see cref="Permits"/> everything else. Two
/// delegates and a permit count are a whole reaper, with no browser anywhere. THAT is what the extraction
/// bought; the line count was never the point.
/// </para>
/// <para>
/// The sweep period is half the idle window with a floor of one second, so every check here waits on the
/// order of a second by construction. They are written to finish as soon as the thing they wait for happens
/// rather than to sleep a fixed time, and the failure budget is generous, because what is being asserted is
/// "does this ever happen" and never "how fast".
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class ReaperTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    /// <summary>A browser nobody has used is closed.</summary>
    [Fact]
    public async Task ReapAsync_BrowserIdleAndNobodyHoldingAPage_ClosesIt()
    {
        var permits = new Permits(4);
        var closed = new TaskCompletionSource();

        using var shutdown = new CancellationTokenSource();

        var reaping = Reaping(permits, isOpen: () => true, closed, shutdown.Token);

        Assert.Same(closed.Task, await Task.WhenAny(closed.Task, Task.Delay(Budget)));

        await shutdown.CancelAsync();
        await reaping;
    }

    /// <summary>A browser somebody is holding a page on is left alone, however idle it looks.</summary>
    /// <remarks>
    /// THE LOAD-BEARING CHECK OF THE WHOLE MECHANISM, and the one that had no way of existing before. The idle
    /// clock only moves when a permit comes BACK, so a caller that took one and is still working looks idle by
    /// every measure the reaper has except the permit it is holding. Closing the browser under it would end
    /// every page in flight — which is the failure the all-or-nothing take exists to prevent, and which
    /// nothing until now asserted.
    /// <para>
    /// Three seconds is three sweeps at this window, not one: a check that waited a single period could pass
    /// against a reaper that simply had not looked yet.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReapAsync_SomebodyHoldingAPage_LeavesTheBrowserAlone()
    {
        var permits = new Permits(4);
        var closed = new TaskCompletionSource();

        using var shutdown = new CancellationTokenSource();

        await permits.WaitAsync(CancellationToken.None);

        var reaping = Reaping(permits, isOpen: () => true, closed, shutdown.Token);

        var looked = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(3)));

        await shutdown.CancelAsync();
        await reaping;

        Assert.NotSame(closed.Task, looked);
    }

    /// <summary>A close that throws still gives every permit back.</summary>
    /// <remarks>
    /// The <c>finally</c> around the close is load-bearing rather than tidy, and this is the check that says
    /// so. Every permit is taken before the browser is closed; a close that throws without giving them back
    /// leaves the pool unable to hand out a single page ever again — a permanently dead pool as the price of
    /// one browser that would not shut down, which is the worse of the two outcomes by a wide margin.
    /// <para>
    /// Both permits, not one: a partial refill is the same dead pool arriving more slowly.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReapAsync_CloseThatThrows_StillGivesThePermitsBack()
    {
        var permits = new Permits(2);
        var attempted = new TaskCompletionSource();

        using var shutdown = new CancellationTokenSource();

        var reaping = new Reaper(
            Idle,
            permits,
            isOpen: () => true,
            closeAsync: () =>
            {
                attempted.TrySetResult();

                throw new InvalidOperationException("the browser would not close");
            },
            NullLogger.Instance).ReapAsync(shutdown.Token);

        Assert.Same(attempted.Task, await Task.WhenAny(attempted.Task, Task.Delay(Budget)));

        await shutdown.CancelAsync();
        await reaping;

        Assert.True(permits.TryEmpty(), "the permits were not given back after a close that threw");
    }

    /// <summary>There is no browser to reap, so nothing is asked of the pool.</summary>
    /// <remarks>
    /// Cheap to assert and worth asserting: the order of the three questions is what keeps the expensive one —
    /// taking every permit, which blocks new callers until it is given back — from being asked on a pool that
    /// has never opened a browser at all.
    /// </remarks>
    [Fact]
    public async Task ReapAsync_NoBrowserOpen_NeverTakesThePermits()
    {
        var permits = new Permits(1);
        var closed = new TaskCompletionSource();

        using var shutdown = new CancellationTokenSource();

        var reaping = Reaping(permits, isOpen: () => false, closed, shutdown.Token);

        var looked = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(3)));

        await shutdown.CancelAsync();
        await reaping;

        Assert.NotSame(closed.Task, looked);
        Assert.True(permits.TryEmpty(), "the permits were taken for a browser that does not exist");
    }

    /// <summary>An idle timeout of zero switches the reaper off rather than making it spin.</summary>
    /// <remarks>
    /// <see cref="PeriodicTimer"/> refuses a period of zero, so without the guard this is not a reaper that
    /// never fires but a constructor that throws — inside a <c>Task.Run</c> in the pool's own constructor,
    /// where the only trace of it would be a faulted task nobody awaits until shutdown.
    /// </remarks>
    [Fact]
    public async Task ReapAsync_IdleTimeoutOfZero_EndsAtOnceWithoutSweeping()
    {
        var permits = new Permits(1);
        var closed = new TaskCompletionSource();

        using var shutdown = new CancellationTokenSource();

        var reaping = new Reaper(
            TimeSpan.Zero,
            permits,
            isOpen: () => true,
            closeAsync: () =>
            {
                closed.TrySetResult();

                return Task.CompletedTask;
            },
            NullLogger.Instance).ReapAsync(shutdown.Token);

        await reaping.WaitAsync(Budget);

        Assert.False(closed.Task.IsCompleted);
    }

    private static Task Reaping(
        Permits permits,
        Func<bool> isOpen,
        TaskCompletionSource closed,
        CancellationToken ct)
        => new Reaper(
            Idle,
            permits,
            isOpen,
            closeAsync: () =>
            {
                closed.TrySetResult();

                return Task.CompletedTask;
            },
            NullLogger.Instance).ReapAsync(ct);
}
