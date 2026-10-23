using Microsoft.Extensions.Time.Testing;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Pins the two lines in <see cref="Permits"/> that nothing else can fail on visibly.
/// </summary>
/// <remarks>
/// The rest of this type is exercised wherever a lease is taken and returned. These two are not: a partial
/// take that forgets to give itself back leaks permits silently — the pool simply hands out fewer pages for
/// the rest of its life, which looks like slowness rather than like a defect — and an idle clock that never
/// moves makes the reaper either blind or trigger-happy without either being visible at the call site.
/// </remarks>
[Trait("Category", "Unit")]
public class PermitsTests
{
    /// <summary>A take that cannot have every permit gives back the ones it took.</summary>
    /// <remarks>
    /// The failure this prevents is cumulative and quiet. Each sweep that meets a busy pool would keep one
    /// permit fewer than it found, so a pool of four would be a pool of three, then of two, with nothing in
    /// the log and no failure anywhere — only a crawl that runs slower every hour it stays up.
    /// <para>
    /// Three were taken and handed back, so the three that were free still are.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Emptied_WhileOnePermitIsHeld_GivesBackEverythingItTook()
    {
        var permits = new Permits(4, TimeProvider.System);

        await permits.WaitAsync(CancellationToken.None);

        Assert.False(permits.TryEmpty());

        for (var page = 0; page < 3; page++)
            Assert.True(permits.WaitAsync(CancellationToken.None).IsCompleted, $"permit {page} went missing");
    }

    /// <summary>The idle clock runs from the moment a page was handed back, not from when the pool was made.</summary>
    /// <remarks>
    /// THE FIRST VERSION OF THIS CHECK PASSED FOR THE WRONG REASON, and it is written down because the same
    /// trap is set for anybody editing it. It asserted "not idle for a whole minute" straight after a return,
    /// which is true of a <see cref="Permits"/> built microseconds earlier whether or not <c>Return</c> moves
    /// the clock at all — the field starts at the moment of construction. Removing the line it was meant to
    /// guard left it green.
    /// <para>
    /// So the clock is made STALE FIRST and the window is chosen to sit between the two instants: after a
    /// hundred milliseconds of doing nothing the pool is idle by a fifty-millisecond window, and the return
    /// has to make it not idle by that same window. With the clock frozen at construction the second
    /// assertion fails, which is what makes this a guard rather than a statement about how fast a test runs.
    /// </para>
    /// <para>
    /// The hundred milliseconds are the test's clock's since 2026-10-23, moved by hand rather than slept.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task IsIdleFor_AfterAPermitComesBack_MeasuresFromThatMoment()
    {
        var clock = new FakeTimeProvider();
        var permits = new Permits(1, clock);
        var window = TimeSpan.FromMilliseconds(50);

        clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.True(permits.IsIdleFor(window), "a pool nobody has touched should read as idle");

        await permits.WaitAsync(CancellationToken.None);

        permits.Return();

        Assert.False(permits.IsIdleFor(window), "the clock did not move when the page was handed back");
    }
}
