using System.Diagnostics;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Politeness;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the gate that answers the one signal a site reads first: request rate.
/// </summary>
public class HostPolitenessTests
{
    /// <summary>
    /// The gap is measured from the moment the previous request finished. Two hundred milliseconds is long
    /// enough to measure without making the suite slow; the assertion allows for timer coarseness.
    /// </summary>
    [Fact]
    public async Task EnterAsync_SecondTurnOnOneHost_WaitsTheConfiguredGap()
    {
        using var politeness = Gate(TimeSpan.FromMilliseconds(200), 1);

        await using (await politeness.EnterAsync(new Uri("https://example.com/a")))
        {
        }

        var started = Stopwatch.GetTimestamp();

        await using (await politeness.EnterAsync(new Uri("https://example.com/b")))
        {
        }

        Assert.True(Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(150));
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
        using var politeness = Gate(TimeSpan.Zero, 1);

        await using (await politeness.EnterAsync(new Uri("https://example.com/a")))
        {
        }

        var started = Stopwatch.GetTimestamp();

        await using (await politeness.EnterAsync(new Uri("https://example.com/b"), TimeSpan.FromMilliseconds(200)))
        {
        }

        Assert.True(Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(150));
    }

    /// <summary>A site asking for a short gap cannot talk a careful caller down to it.</summary>
    [Fact]
    public async Task EnterAsync_SiteAsksForAShorterGap_KeepsTheConfiguredOne()
    {
        using var politeness = Gate(TimeSpan.FromMilliseconds(200), 1);

        await using (await politeness.EnterAsync(new Uri("https://example.com/a")))
        {
        }

        var started = Stopwatch.GetTimestamp();

        await using (await politeness.EnterAsync(new Uri("https://example.com/b"), TimeSpan.Zero))
        {
        }

        Assert.True(Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(150));
    }

    /// <summary>
    /// One gap for every host is the wrong shape when a search engine and a newspaper are read in the same
    /// minute: a host named in the settings gets its own, longer one.
    /// </summary>
    [Fact]
    public async Task EnterAsync_HostWithItsOwnGap_WaitsThatLong()
    {
        using var politeness = Gate(TimeSpan.Zero, 1, ("engine.example", TimeSpan.FromMilliseconds(200)));

        await using (await politeness.EnterAsync(new Uri("https://engine.example/a")))
        {
        }

        var started = Stopwatch.GetTimestamp();

        await using (await politeness.EnterAsync(new Uri("https://engine.example/b")))
        {
        }

        Assert.True(Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(150));
    }

    /// <summary>A host beneath a named one inherits its gap, so one line covers a site together with its subdomains.</summary>
    [Fact]
    public async Task EnterAsync_HostBeneathANamedOne_InheritsItsGap()
    {
        using var politeness = Gate(TimeSpan.Zero, 1, ("example.com", TimeSpan.FromMilliseconds(200)));

        await using (await politeness.EnterAsync(new Uri("https://news.example.com/a")))
        {
        }

        var started = Stopwatch.GetTimestamp();

        await using (await politeness.EnterAsync(new Uri("https://news.example.com/b")))
        {
        }

        Assert.True(Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(150));
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

    private static HostPoliteness Gate(
        TimeSpan minDelay,
        int maxConcurrent,
        params (string Host, TimeSpan Gap)[] gentler)
        => new(Options.Create(new PolitenessOptions
        {
            MinDelayPerHost = minDelay,
            MaxConcurrentPerHost = maxConcurrent,
            HostDelays = gentler.ToDictionary(named => named.Host, named => named.Gap, StringComparer.OrdinalIgnoreCase)
        }));
}
