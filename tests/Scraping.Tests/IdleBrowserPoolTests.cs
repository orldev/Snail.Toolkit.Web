using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers the pool closing a browser it launched once nobody has used it for the idle timeout.
/// </summary>
/// <remarks>
/// <see cref="ReaperTests"/> holds the reaper's rules; what only the pool can show is that it hands the reaper
/// the right two answers — whether a browser is open, and how to close it — and that a browser closed for
/// idleness is opened again for the next caller. Until 2026-10-23 that needed a launched browser and a wait of
/// minutes, which is why nothing checked it: the pool built its own driver and read the system's clock. The
/// internal constructor takes both, so the browser here is a proxy and the minutes are the test's.
/// </remarks>
[Trait("Category", "Unit")]
public class IdleBrowserPoolTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    /// <summary>A browser nobody has used for the idle timeout is closed, and the next caller gets a new one.</summary>
    /// <remarks>
    /// The reaper runs on a task the pool starts, so the clock is moved a sweep at a time with a moment to
    /// look in between, until the browser closes or the sweeps run out; a sweep moved before the reaper's
    /// timer exists is simply not counted.
    /// </remarks>
    [Fact]
    public async Task RentAsync_AfterTheBrowserWasIdleForTheTimeout_GivesANewBrowser()
    {
        var clock = new FakeTimeProvider();
        var playwright = Launching.Playwright;

        await using var pool = Pool(clock, playwright);

        await using (await pool.RentAsync())
        {
        }

        var first = Launching.Of(playwright).Single();

        for (var sweep = 0; sweep < 50 && !first.IsClosed; sweep++)
        {
            clock.Advance(Idle / 2);

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        Assert.True(first.IsClosed, "the idle browser was never closed");

        await using var lease = await pool.RentAsync().WaitAsync(Budget);

        Assert.Equal(2, Launching.Of(playwright).Count);
    }

    /// <summary>A browser in use is not closed however long the clock runs.</summary>
    [Fact]
    public async Task RentAsync_WhileALeaseIsHeld_TheBrowserIsLeftOpen()
    {
        var clock = new FakeTimeProvider();
        var playwright = Launching.Playwright;

        await using var pool = Pool(clock, playwright);
        await using var lease = await pool.RentAsync();

        for (var sweep = 0; sweep < 10; sweep++)
        {
            clock.Advance(Idle);

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        Assert.False(Launching.Of(playwright).Single().IsClosed, "the browser was closed under a held lease");
    }

    private static BrowserPool Pool(TimeProvider clock, IPlaywright playwright)
        => new(
            Options.Create(new ScrapingOptions { BrowserIdleTimeout = Idle }),
            NullLogger<BrowserPool>.Instance,
            clock,
            () => Task.FromResult(playwright));

    /// <summary>A Playwright whose browser type launches proxies, and remembers every one it launched.</summary>
    /// <remarks>
    /// Proxies for the reason the rest of this suite uses them: <see cref="IPlaywright"/>,
    /// <see cref="IBrowserType"/> and <see cref="IBrowser"/> have dozens of members and the pool touches four.
    /// Unsealed because the generated proxy derives from it.
    /// </remarks>
    [SuppressMessage("Performance", "CA1852", Justification = "DispatchProxy derives the proxy from it at run time.")]
    private class Launching : DispatchProxy
    {
        private readonly List<Launched> _launched = [];

        public static IPlaywright Playwright => DispatchProxy.Create<IPlaywright, Launching>();

        public static IReadOnlyList<Launched> Of(IPlaywright playwright)
        {
            var launching = (Launching)(object)playwright;

            lock (launching._launched)
                return [.. launching._launched];
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_Chromium" or "get_Firefox" or "get_Webkit" => Types.Of(this),
            _ => null
        };

        internal IBrowser Launch()
        {
            var browser = Launched.Browser();

            lock (_launched)
                _launched.Add(Launched.Of(browser));

            return browser;
        }

        /// <summary>The browser type the proxy hands out, launching through it.</summary>
        [SuppressMessage("Performance", "CA1852", Justification = "DispatchProxy derives the proxy from it at run time.")]
        private class Types : DispatchProxy
        {
            private Launching? _owner;

            public static IBrowserType Of(Launching owner)
            {
                var type = DispatchProxy.Create<IBrowserType, Types>();

                ((Types)(object)type)._owner = owner;

                return type;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
            {
                nameof(IBrowserType.LaunchAsync) => Task.FromResult(_owner!.Launch()),
                _ => null
            };
        }
    }

    /// <summary>A launched browser that is connected until it is closed.</summary>
    [SuppressMessage("Performance", "CA1852", Justification = "DispatchProxy derives the proxy from it at run time.")]
    private class Launched : DispatchProxy
    {
        private int _isClosed;

        public bool IsClosed => Volatile.Read(ref _isClosed) == 1;

        public static IBrowser Browser() => DispatchProxy.Create<IBrowser, Launched>();

        public static Launched Of(IBrowser browser) => (Launched)(object)browser;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name is nameof(IBrowser.CloseAsync))
                Interlocked.Exchange(ref _isClosed, 1);

            return targetMethod?.Name switch
            {
                "get_IsConnected" => !IsClosed,
                nameof(IBrowser.CloseAsync) => Task.CompletedTask,
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                _ => null
            };
        }
    }
}
