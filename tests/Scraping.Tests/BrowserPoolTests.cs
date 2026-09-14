using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Browsers;
using Snail.Toolkit.Web.Tests.Browsers;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers what happens to a browser slot when something on the way back out fails.
/// </summary>
/// <remarks>
/// A slot is either in the channel or held by someone, and the pool has no way of noticing a third state. The
/// checks here are about the paths that produce one, because none of them is reachable from the crawl tests:
/// the defaults hand out one browser, a lease is held for a whole visit, and no test in the suite has ever
/// disposed a pool with a caller still waiting on it.
/// </remarks>
[Collection(SiteCollection.Name)]
[Trait("Category", "Integration")]
public class BrowserPoolTests(SiteFixture site)
{
    /// <summary>A context that refuses to close still gives the browser back.</summary>
    /// <remarks>
    /// Disposal releases two things in order, and the second one is the browser. A browser server reached over
    /// a socket refuses at the most ordinary moment there is — the connection dropped while the page was open —
    /// and the refusal arrives here, from the first of the two.
    /// </remarks>
    [Fact]
    public async Task DisposeAsync_AContextThatRefusesToClose_StillGivesTheBrowserBack()
    {
        var lease = new Borrowed();
        var visit = new Visit(lease, Refusing.Context, Nothing.Page, Chrome);

        await Assert.ThrowsAsync<PlaywrightException>(async () => await visit.DisposeAsync());

        Assert.True(lease.IsReturned);
    }

    /// <summary>
    /// The pool survives such a visit: the next caller is served rather than left waiting for a slot nobody
    /// holds.
    /// </summary>
    /// <remarks>
    /// This is the consequence the check above only describes. One browser is the default, so a single lost
    /// slot is the whole pool, and the process does not fail — it stops reading pages and says nothing.
    /// </remarks>
    [Fact]
    public async Task RentAsync_AfterAVisitWhoseContextRefusedToClose_StillHandsOutABrowser()
    {
        await using var pool = Pool();

        var visit = new Visit(await pool.RentAsync(), Refusing.Context, Nothing.Page, Chrome);

        await Assert.ThrowsAsync<PlaywrightException>(async () => await visit.DisposeAsync());

        var again = pool.RentAsync();

        Assert.Same(again, await Task.WhenAny(again, Task.Delay(TimeSpan.FromSeconds(10))));

        await (await again).DisposeAsync();
    }

    /// <summary>Four pages are open at once, which is what the ceiling now means.</summary>
    /// <remarks>
    /// The ceiling used to be browser processes and the default was one, so this many pages could not be open
    /// at once whatever any other setting said — the effective parallelism of the whole toolkit was one, and
    /// nothing executed the concurrent paths. A page costs a quarter of a process and opens in a third of the
    /// time (<c>probes/contexts</c>, 2026-09-14), so a page is what is rationed.
    /// </remarks>
    [Fact]
    public async Task RentAsync_AsManyCallersAsTheCeiling_AreAllServedAtOnce()
    {
        await using var pool = Pool(4);

        var leases = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => pool.RentAsync()));

        Assert.Equal(4, leases.Distinct().Count());

        foreach (var lease in leases)
            await lease.DisposeAsync();
    }

    /// <summary>Four pages really are read at once, on one browser, each seeing its own document.</summary>
    /// <remarks>
    /// The check above counts permissions, which a semaphore would satisfy without a browser being involved at
    /// all. This one opens the contexts and reads through them, because the claim being made is not that the
    /// pool allows four but that four pages fit in one process — which is the measured claim the whole change
    /// rests on.
    /// </remarks>
    [Fact]
    public async Task RentAsync_FourLeasesAtOnce_EachReadsThroughItsOwnContext()
    {
        await using var pool = Pool(4);

        var leases = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => pool.RentAsync()));

        var headings = await Task.WhenAll(leases.Select(async lease =>
        {
            await using var context = await lease.Browser.NewContextAsync();

            var page = await context.NewPageAsync();

            await page.GotoAsync($"{SiteFixture.BaseUrl}/hub.html");

            return await page.Locator("h1").InnerTextAsync();
        }));

        foreach (var lease in leases)
            await lease.DisposeAsync();

        Assert.Equal(["Everything we publish", "Everything we publish", "Everything we publish", "Everything we publish"], headings);
    }

    /// <summary>And the one past the ceiling waits, which is what makes the check above mean something.</summary>
    /// <remarks>
    /// Without it a pool that handed out permission to everyone would pass: the point is not that four succeed
    /// but that four is the number, and memory is the reason there is a number at all.
    /// </remarks>
    [Fact]
    public async Task RentAsync_OneCallerPastTheCeiling_Waits()
    {
        await using var pool = Pool(4);

        var held = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => pool.RentAsync()));

        var waiting = pool.RentAsync();

        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(2))));

        foreach (var lease in held)
            await lease.DisposeAsync();

        Assert.Same(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(10))));
        await (await waiting).DisposeAsync();
    }

    /// <summary>A caller queued for a browser is told the pool is gone, the same as one arriving after.</summary>
    /// <remarks>
    /// Shutdown arrives while work is in flight — that is what shutdown is — and a caller waiting on the free
    /// channel used to be told nothing at all. <c>RentAsync</c> has always guarded its entrance against a
    /// disposed pool, so the one caller it could not refuse was the one that had already come in.
    /// </remarks>
    [Fact]
    public async Task RentAsync_WaitingWhenThePoolIsDisposed_IsToldThePoolIsGone()
    {
        var pool = Pool();

        await pool.RentAsync();

        var waiting = pool.RentAsync();

        await pool.DisposeAsync();

        Assert.Same(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(10))));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
    }

    private BrowserPool Pool(int pages = 1)
        => new(Options.Create(new ScrapingOptions
        {
            BrowserServer = site.BrowserServer,
            Engine = site.Engine,
            MaxDegreeOfParallelism = pages,
            NavigationTimeoutMs = 20_000
        }), NullLogger<BrowserPool>.Instance);

    private static WebIdentity Chrome => new()
    {
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        Platform = "Windows",
        NavigatorPlatform = "Win32",
        Brand = "Google Chrome",
        MajorVersion = "124",
        Locale = "en-US",
        Timezone = "UTC",
        AcceptLanguage = "en-US,en;q=0.9",
        Languages = ["en-US", "en"]
    };

    /// <summary>A lease that remembers whether it was given back.</summary>
    private sealed class Borrowed : IBrowserLease
    {
        public bool IsReturned { get; private set; }

        public IBrowser Browser => throw new NotSupportedException("The browser is never touched here.");

        public ValueTask DisposeAsync()
        {
            IsReturned = true;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A context whose disposal fails the way a dropped connection fails.</summary>
    /// <remarks>
    /// Written as a proxy rather than a class because <see cref="IBrowserContext"/> has some seventy members
    /// and this check touches one of them. Playwright ships no test double, and the suite has no mocking
    /// library — one that answers a single method and nothing else says more plainly what is being measured.
    /// Unsealed because the generated proxy derives from it.
    /// </remarks>
    private class Refusing : DispatchProxy
    {
        public static IBrowserContext Context => DispatchProxy.Create<IBrowserContext, Refusing>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name is nameof(IAsyncDisposable.DisposeAsync)
                ? ValueTask.FromException(new PlaywrightException("Target page, context or browser has been closed"))
                : null;
    }

    /// <summary>A page that is carried and never asked anything.</summary>
    private class Nothing : DispatchProxy
    {
        public static IPage Page => DispatchProxy.Create<IPage, Nothing>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
