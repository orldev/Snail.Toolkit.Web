using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// Covers the rest a refused provider is given: how long, how it grows, and what ends it.
/// </summary>
public class SearchCooldownTests
{
    private static readonly DateTimeOffset Evening = new(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RestingUntil_ProviderNeverRefused_AnswersNothing()
        => Assert.Null(Cooldown(new Clock(Evening)).RestingUntil("A"));

    [Fact]
    public void Refused_FirstTime_RestsForTheConfiguredCooldown()
    {
        var cooldown = Cooldown(new Clock(Evening));

        Assert.Equal(TimeSpan.FromMinutes(5), cooldown.Refused("A"));
        Assert.Equal(Evening.AddMinutes(5), cooldown.RestingUntil("A"));
    }

    [Fact]
    public void RestingUntil_OnceTheRestHasRunOut_AnswersNothing()
    {
        var clock = new Clock(Evening);
        var cooldown = Cooldown(clock);

        cooldown.Refused("A");
        clock.Now += TimeSpan.FromMinutes(5);

        Assert.Null(cooldown.RestingUntil("A"));
    }

    /// <summary>An engine that refuses, waits out its rest and refuses again is saying the rest was too short.</summary>
    [Fact]
    public void Refused_AgainAfterTheRest_DoublesIt()
    {
        var clock = new Clock(Evening);
        var cooldown = Cooldown(clock);

        cooldown.Refused("A");
        clock.Now += TimeSpan.FromMinutes(5);

        Assert.Equal(TimeSpan.FromMinutes(10), cooldown.Refused("A"));

        clock.Now += TimeSpan.FromMinutes(10);

        Assert.Equal(TimeSpan.FromMinutes(20), cooldown.Refused("A"));
    }

    /// <summary>
    /// Refusals that arrive while the rest still runs are the same refusal: requests already in flight when the
    /// engine closed. Counting each as a further one grew five minutes to the ceiling inside one batch.
    /// </summary>
    [Fact]
    public void Refused_AgainWhileStillResting_DoesNotLengthenTheRest()
    {
        var clock = new Clock(Evening);
        var cooldown = Cooldown(clock);

        cooldown.Refused("A");
        clock.Now += TimeSpan.FromMinutes(1);

        Assert.Equal(TimeSpan.FromMinutes(4), cooldown.Refused("A"));
        Assert.Equal(Evening.AddMinutes(5), cooldown.RestingUntil("A"));

        clock.Now += TimeSpan.FromMinutes(4);

        Assert.Equal(TimeSpan.FromMinutes(10), cooldown.Refused("A"));
    }

    [Fact]
    public void Refused_ManyTimes_NeverRestsLongerThanTheCeiling()
    {
        var clock = new Clock(Evening);
        var cooldown = Cooldown(clock, ceiling: TimeSpan.FromMinutes(12));

        var rests = Enumerable.Range(0, 40).Select(_ =>
        {
            var rest = cooldown.Refused("A");

            clock.Now += rest;

            return rest;
        }).ToArray();

        Assert.Equal(TimeSpan.FromMinutes(12), rests[^1]);
        Assert.All(rests, rest => Assert.True(rest <= TimeSpan.FromMinutes(12)));
    }

    /// <summary>
    /// An answer — any answer — is the engine saying it is open again, and the run of refusals goes with it:
    /// the next refusal starts over at the configured rest rather than where the last run left off.
    /// </summary>
    [Fact]
    public void Answered_AfterRefusals_EndsTheRestAndForgetsTheRun()
    {
        var cooldown = Cooldown(new Clock(Evening));

        cooldown.Refused("A");
        cooldown.Refused("A");
        cooldown.Answered("A");

        Assert.Null(cooldown.RestingUntil("A"));
        Assert.Equal(TimeSpan.FromMinutes(5), cooldown.Refused("A"));
    }

    [Fact]
    public void Refused_OneProvider_LeavesTheOthersAlone()
    {
        var cooldown = Cooldown(new Clock(Evening));

        cooldown.Refused("A");

        Assert.Null(cooldown.RestingUntil("B"));
    }

    private static SearchCooldown Cooldown(TimeProvider clock, TimeSpan? ceiling = null)
        => new(
            Options.Create(new SearchOptions
            {
                RefusalCooldown = TimeSpan.FromMinutes(5),
                MaxRefusalCooldown = ceiling ?? TimeSpan.FromHours(1)
            }),
            clock);
}
