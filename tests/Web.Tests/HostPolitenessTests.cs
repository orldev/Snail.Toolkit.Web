using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Snail.Toolkit.Web.Politeness;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the gate that answers the one signal a site reads first: request rate.
/// </summary>
public class HostPolitenessTests
{
    /// <summary>
    /// The gap is measured from the moment the previous request finished: until it has passed the gate holds
    /// the next caller, and when it has passed the caller goes through.
    /// </summary>
    /// <remarks>
    /// Time is moved by hand rather than waited out. Until 2026-09-20 these assertions read a stopwatch, so
    /// each of them was also an assertion about how loaded the machine was — and the full run failed on one of
    /// them twice in six runs while the same test alone never did.
    /// </remarks>
    [Fact]
    public async Task EnterAsync_SecondTurnOnOneHost_WaitsTheConfiguredGap()
    {
        var clock = new FakeTimeProvider();
        using var politeness = Gate(TimeSpan.FromSeconds(30), 1, clock);

        await using (await politeness.EnterAsync(new Uri("https://example.com/a")))
        {
        }

        var entering = politeness.EnterAsync(new Uri("https://example.com/b"));

        Assert.False(await Opened(entering));

        clock.Advance(TimeSpan.FromSeconds(30));

        await entering.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Limits are per host, so a crawl spread across many sites keeps its throughput.</summary>
    [Fact]
    public async Task EnterAsync_AnotherHost_DoesNotWaitForTheFirst()
    {
        using var politeness = Gate(TimeSpan.FromSeconds(30), 1);

        await politeness.EnterAsync(new Uri("https://first.example/a"));

        var entering = politeness.EnterAsync(new Uri("https://second.example/a"));

        Assert.Same(entering, await Task.WhenAny(entering, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    /// <summary>
    /// The gap a site asks for in its <c>Crawl-delay</c>, which is the whole point of parsing that directive.
    /// </summary>
    [Fact]
    public async Task EnterAsync_SiteAsksForALongerGap_WaitsThatLong()
    {
        var clock = new FakeTimeProvider();
        using var politeness = Gate(TimeSpan.Zero, 1, clock);

        await using (await politeness.EnterAsync(new Uri("https://example.com/a")))
        {
        }

        var entering = politeness.EnterAsync(new Uri("https://example.com/b"), TimeSpan.FromSeconds(30));

        Assert.False(await Opened(entering));

        clock.Advance(TimeSpan.FromSeconds(30));

        await entering.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>A site asking for a short gap cannot talk a careful caller down to it.</summary>
    [Fact]
    public async Task EnterAsync_SiteAsksForAShorterGap_KeepsTheConfiguredOne()
    {
        var clock = new FakeTimeProvider();
        using var politeness = Gate(TimeSpan.FromSeconds(30), 1, clock);

        await using (await politeness.EnterAsync(new Uri("https://example.com/a")))
        {
        }

        var entering = politeness.EnterAsync(new Uri("https://example.com/b"), TimeSpan.Zero);

        Assert.False(await Opened(entering));

        clock.Advance(TimeSpan.FromSeconds(30));

        await entering.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// One gap for every host is the wrong shape when a search engine and a newspaper are read in the same
    /// minute: a host named in the settings gets its own, longer one.
    /// </summary>
    [Fact]
    public async Task EnterAsync_HostWithItsOwnGap_WaitsThatLong()
    {
        var clock = new FakeTimeProvider();
        using var politeness = Gate(TimeSpan.Zero, 1, clock, ("engine.example", TimeSpan.FromSeconds(30)));

        await using (await politeness.EnterAsync(new Uri("https://engine.example/a")))
        {
        }

        var entering = politeness.EnterAsync(new Uri("https://engine.example/b"));

        Assert.False(await Opened(entering));

        clock.Advance(TimeSpan.FromSeconds(30));

        await entering.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>A host beneath a named one inherits its gap, so one line covers a site together with its subdomains.</summary>
    [Fact]
    public async Task EnterAsync_HostBeneathANamedOne_InheritsItsGap()
    {
        var clock = new FakeTimeProvider();
        using var politeness = Gate(TimeSpan.Zero, 1, clock, ("example.com", TimeSpan.FromSeconds(30)));

        await using (await politeness.EnterAsync(new Uri("https://news.example.com/a")))
        {
        }

        var entering = politeness.EnterAsync(new Uri("https://news.example.com/b"));

        Assert.False(await Opened(entering));

        clock.Advance(TimeSpan.FromSeconds(30));

        await entering.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>The cut is at a dot: <c>ample.com</c> is not a parent of <c>example.com</c>, however it ends.</summary>
    [Fact]
    public async Task EnterAsync_HostThatMerelyEndsLikeANamedOne_KeepsTheDefault()
    {
        using var politeness = Gate(TimeSpan.Zero, 1, ("ample.com", TimeSpan.FromSeconds(30)));

        await using (await politeness.EnterAsync(new Uri("https://example.com/a")))
        {
        }

        var entering = politeness.EnterAsync(new Uri("https://example.com/b"));

        Assert.Same(entering, await Task.WhenAny(entering, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task EnterAsync_HostAtItsConcurrencyLimit_MakesTheNextCallerWait()
    {
        using var politeness = Gate(TimeSpan.Zero, 1);

        await politeness.EnterAsync(new Uri("https://example.com/a"));

        var waiting = politeness.EnterAsync(new Uri("https://example.com/b"));

        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMilliseconds(200))));
    }

    /// <summary>A gap the site asked for binds every caller, not the one that was told.</summary>
    /// <remarks>
    /// The point of writing it into the gate rather than into a retry delay: a <c>429</c> answered to one
    /// worker is not a fact about that worker, and two others hitting the same host would otherwise walk
    /// straight into it.
    /// </remarks>
    [Fact]
    public async Task Rest_HostThatAskedToBeLeftAlone_MakesTheNextCallerWait()
    {
        using var politeness = Gate(TimeSpan.Zero, 2);

        politeness.Rest(new Uri("https://example.com/refused"), TimeSpan.FromSeconds(5));

        var waiting = politeness.EnterAsync(new Uri("https://example.com/other"));

        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMilliseconds(200))));
    }

    /// <summary>A rest longer than any timer can wait is waited, not thrown at the caller.</summary>
    /// <remarks>
    /// MEASURED on 2026-09-20: a site answering <c>Retry-After</c> of a hundred days made every later
    /// <c>EnterAsync</c> to that host throw <see cref="ArgumentOutOfRangeException"/> out of
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>, whose ceiling is about forty-nine days. One header
    /// broke the host until the process restarted, and the exception named a timer rather than the site.
    /// </remarks>
    [Fact]
    public async Task Rest_LongerThanATimerCanWait_KeepsTheCallerWaitingRatherThanThrowing()
    {
        using var politeness = Gate(TimeSpan.Zero, 1);
        var host = new Uri("https://example.com/refused");

        politeness.Rest(host, TimeSpan.FromDays(100));
        politeness.Rest(host, TimeSpan.MaxValue);

        using var shortly = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => politeness.EnterAsync(host, ct: shortly.Token));
        Assert.Equal(DateTimeOffset.MaxValue, politeness.OpensAt(host));
    }

    /// <summary>Another host is none of its business.</summary>
    [Fact]
    public async Task Rest_OneHost_LeavesTheOthersAlone()
    {
        using var politeness = Gate(TimeSpan.Zero, 1);

        politeness.Rest(new Uri("https://example.com/refused"), TimeSpan.FromSeconds(5));

        var entering = politeness.EnterAsync(new Uri("https://elsewhere.example/a"));

        Assert.Same(entering, await Task.WhenAny(entering, Task.Delay(TimeSpan.FromSeconds(2))));
    }

    /// <summary>Being asked twice never shortens the wait.</summary>
    [Fact]
    public async Task Rest_AShorterGapAfterALongerOne_KeepsTheLongerOne()
    {
        using var politeness = Gate(TimeSpan.Zero, 1);
        var host = new Uri("https://example.com/refused");

        politeness.Rest(host, TimeSpan.FromSeconds(5));
        politeness.Rest(host, TimeSpan.FromMilliseconds(1));

        var waiting = politeness.EnterAsync(host);

        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMilliseconds(200))));
    }

    /// <summary>A host nobody has spoken to in a while stops being remembered.</summary>
    /// <remarks>
    /// One gate per host and nothing that removed one: a crawl across many sites grew the map for as long as
    /// the process ran, and the hosts of the web are not a finite set. Forgetting costs nothing here, because
    /// an idle gate answers exactly what a fresh one would.
    /// </remarks>
    [Fact]
    public async Task EnterAsync_HostsNobodyHasSpokenToSince_AreForgotten()
    {
        using var politeness = Forgetful(TimeSpan.Zero);

        foreach (var host in Enumerable.Range(0, 20))
            await using (await politeness.EnterAsync(new Uri($"https://host-{host}.example/a")))
            {
            }

        await using (await politeness.EnterAsync(new Uri("https://the-one-that-matters.example/a")))
        {
        }

        Assert.Equal(1, politeness.RememberedHosts);
    }

    /// <summary>A host being held back is never forgotten, however eager the forgetting.</summary>
    /// <remarks>
    /// Asked of the gate rather than of the map: were the entry dropped, the next caller would meet a fresh
    /// gate with nothing to say and walk straight into a site that had just asked to be left alone. That is
    /// the failure this check is for, and counting entries would not have seen it.
    /// </remarks>
    [Fact]
    public async Task Rest_HostThatIsStillResting_IsNotForgotten()
    {
        using var politeness = Forgetful(TimeSpan.Zero);
        var resting = new Uri("https://refused.example/a");

        politeness.Rest(resting, TimeSpan.FromSeconds(5));

        foreach (var host in Enumerable.Range(0, 20))
            await using (await politeness.EnterAsync(new Uri($"https://host-{host}.example/a")))
            {
            }

        var waiting = politeness.EnterAsync(resting);

        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMilliseconds(200))));
    }

    /// <summary>Neither is a host with a request still in flight.</summary>
    /// <remarks>
    /// The turn is held open on purpose. A dropped entry would hand the next caller a gate with every slot
    /// free while the first request is still running, which is the one thing the concurrency limit exists to
    /// prevent.
    /// </remarks>
    [Fact]
    public async Task EnterAsync_HostWithARequestInFlight_IsNotForgotten()
    {
        using var politeness = Forgetful(TimeSpan.Zero);
        var busy = new Uri("https://busy.example/a");

        await politeness.EnterAsync(busy);

        foreach (var host in Enumerable.Range(0, 20))
            await using (await politeness.EnterAsync(new Uri($"https://host-{host}.example/a")))
            {
            }

        var waiting = politeness.EnterAsync(busy);

        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMilliseconds(200))));
    }

    private static HostPoliteness Forgetful(TimeSpan forgetAfter)
        => new(Options.Create(new PolitenessOptions
        {
            MinDelayPerHost = TimeSpan.Zero,
            MaxConcurrentPerHost = 1,
            ForgetHostAfter = forgetAfter
        }));

    /// <summary>Whether a caller got through without anybody moving the clock.</summary>
    /// <remarks>
    /// A gate that is shut cannot open on its own under a clock nothing advances, so the wait here bounds a
    /// mistake rather than a machine: it can only be reached by the gate letting somebody through early.
    /// </remarks>
    private static async Task<bool> Opened(Task<IAsyncDisposable> entering)
        => entering == await Task.WhenAny(entering, Task.Delay(TimeSpan.FromMilliseconds(50)));

    private static HostPoliteness Gate(
        TimeSpan minDelay,
        int maxConcurrent,
        params (string Host, TimeSpan Gap)[] gentler)
        => Gate(minDelay, maxConcurrent, TimeProvider.System, gentler);

    private static HostPoliteness Gate(
        TimeSpan minDelay,
        int maxConcurrent,
        TimeProvider clock,
        params (string Host, TimeSpan Gap)[] gentler)
        => new(Options.Create(new PolitenessOptions
        {
            MinDelayPerHost = minDelay,
            MaxConcurrentPerHost = maxConcurrent,
            HostDelays = gentler.ToDictionary(named => named.Host, named => named.Gap, StringComparer.OrdinalIgnoreCase)
        }), clock);
}
