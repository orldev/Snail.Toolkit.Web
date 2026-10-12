using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Shuts the pool down while it is still opening the browser, which is the one moment nothing has ever tested.
/// </summary>
/// <remarks>
/// <c>BrowserPool</c> is careful about shutdown in the place it was made careful: a caller queued for a page
/// is released by the shutdown token, and the page semaphore is deliberately left undisposed so that a lease
/// returned afterwards does not meet a disposed object — there is a paragraph saying so. The same reasoning
/// was never applied to the second gate. <c>_opening</c> is disposed, and <c>LiveAsync</c> waits on it with
/// the CALLER's token, which is not linked to the shutdown at all.
/// <para>
/// No browser is needed to reach it, which is why these checks exist outside the integration fixtures: a
/// socket that accepts a connection and then says nothing holds <c>ConnectAsync</c> open for as long as the
/// window needs to be. That is also exactly what a browser server under load looks like.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class BrowserPoolShutdownTests
{
    /// <summary>A caller queued behind the opening browser is told the pool closed, by the pool.</summary>
    /// <remarks>
    /// Two callers arrive on a cold pool: the first takes <c>_opening</c> and starts connecting, the second
    /// waits on it. Disposing then disposes that semaphore under the second caller, and what reaches it is
    /// <see cref="ObjectDisposedException"/> naming <c>SemaphoreSlim</c> — an object it has never heard of,
    /// thrown from inside a method it called on something else.
    /// <para>
    /// The pool already answers this correctly one gate earlier: <c>QueuedAsync</c> links the caller's token
    /// with the shutdown and translates what comes back into <c>ObjectDisposedException(nameof(BrowserPool))</c>,
    /// with a remark explaining that a caller arriving after disposal and one already queued when it began
    /// must be told the same thing. This is the same caller, one gate further in.
    /// </para>
    /// <para>
    /// The name rather than the exact spelling of it: a caller refused by the guard at the top of RentAsync is told
    /// the full type name and one refused at the opening gate is told nameof, and which of the two it meets is a
    /// matter of how far it got before the shutdown landed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RentAsync_QueuedBehindAnOpeningBrowserWhenThePoolCloses_IsToldThePoolClosed()
    {
        using var stalling = new Stalling();

        var pool = Pool(stalling);

        var first = pool.RentAsync();
        var second = pool.RentAsync();

        await stalling.Connected;

        await pool.DisposeAsync();

        var refusal = await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await second);

        Assert.Contains(nameof(BrowserPool), refusal.ObjectName, StringComparison.Ordinal);

        Observe(first);
    }

    /// <summary>Shutting down abandons a browser that is still being opened.</summary>
    /// <remarks>
    /// <c>LiveAsync</c> and <c>OpenedAsync</c> take the caller's token and nothing else, so a connection begun
    /// before <c>DisposeAsync</c> runs to its own timeout afterwards — forty-five seconds at the defaults,
    /// against a pool that has already closed and awaited everything it thinks it owns.
    /// <para>
    /// The cost when that connection SUCCEEDS is the one worth stating: <c>LiveAsync</c> ends in
    /// <c>Volatile.Write(ref _browser, opened)</c>, which lands after <c>CloseAsync</c> has already read the
    /// field and written null. Nothing closes that browser afterwards and nothing can — the pool is gone, and
    /// the process it launched or the connection it holds outlives it.
    /// </para>
    /// <para>
    /// Well inside the connect timeout the pool was given, so finishing here means the shutdown ended it rather than
    /// the timeout running out on its own.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RentAsync_OpeningABrowserWhenThePoolCloses_DoesNotCarryOnOpeningIt()
    {
        using var stalling = new Stalling();

        var pool = Pool(stalling);

        var opening = pool.RentAsync();

        await stalling.Connected;

        await pool.DisposeAsync();

        var ended = Ended(opening);

        Assert.Same(ended, await Task.WhenAny(ended, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    /// <summary>Runs a rent to whatever end it comes to, since these checks are about when, not which.</summary>
    /// <remarks>
    /// A pool shutting down under a caller may answer in several ways; which one is the subject of the checks above
    /// rather than of this helper.
    /// </remarks>
    private static async Task Ended(Task<IBrowserLease> renting)
    {
        try
        {
            await using var lease = await renting;
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Keeps a rent nobody is waiting for from ending as an unobserved exception.</summary>
    private static void Observe(Task<IBrowserLease> renting)
        => _ = renting.ContinueWith(
            ended => ended.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static BrowserPool Pool(Stalling stalling)
        => new(
            Options.Create(new ScrapingOptions
            {
                BrowserServer = stalling.Address,
                MaxDegreeOfParallelism = 4,
                NavigationTimeoutMs = 15_000
            }),
            NullLogger<BrowserPool>.Instance);

    /// <summary>A socket that accepts a connection and then says nothing, which is a browser server under load.</summary>
    /// <remarks>
    /// Held for the life of the test: closing it would end the connection and let the client fail early, which is the
    /// opposite of the window these checks need.
    /// </remarks>
    private sealed class Stalling : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Stalling()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            Address = $"ws://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";

            _ = AcceptAsync();
        }

        /// <summary>The endpoint to point a pool at.</summary>
        public string Address { get; }

        /// <summary>Completes once somebody has actually connected, so a test never races the socket.</summary>
        public Task Connected => _connected.Task;

        public void Dispose() => _listener.Dispose();

        private async Task AcceptAsync()
        {
            try
            {
                using var held = await _listener.AcceptTcpClientAsync();

                _connected.TrySetResult();

                await _connected.Task;
                await Task.Delay(TimeSpan.FromMinutes(2));
            }
            catch (Exception)
            {
                _connected.TrySetResult();
            }
        }
    }
}
