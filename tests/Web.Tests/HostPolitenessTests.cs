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

    [Fact]
    public async Task EnterAsync_HostAtItsConcurrencyLimit_MakesTheNextCallerWait()
    {
        using var politeness = Gate(TimeSpan.Zero, 1);

        await politeness.EnterAsync(new Uri("https://example.com/a"));

        var waiting = politeness.EnterAsync(new Uri("https://example.com/b"));

        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMilliseconds(200))));
    }

    private static HostPoliteness Gate(TimeSpan minDelay, int maxConcurrent)
        => new(Options.Create(new PolitenessOptions
        {
            MinDelayPerHost = minDelay,
            MaxConcurrentPerHost = maxConcurrent
        }));
}
