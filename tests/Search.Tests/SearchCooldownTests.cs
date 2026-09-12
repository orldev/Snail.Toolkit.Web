using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// Covers the rest a refused provider is given: how long, how it grows, and what ends it.
/// </summary>
public class SearchCooldownTests
{
    private static readonly DateTimeOffset Evening = new(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsResting_ProviderNeverRefused_IsFalse()
        => Assert.False(Cooldown(new Clock(Evening)).IsResting("A"));

    [Fact]
    public void Refused_FirstTime_RestsForTheConfiguredCooldown()
    {
        var cooldown = Cooldown(new Clock(Evening));

        Assert.Equal(TimeSpan.FromMinutes(5), cooldown.Refused("A"));
        Assert.True(cooldown.IsResting("A"));
    }

    [Fact]
    public void IsResting_OnceTheRestHasRunOut_IsFalse()
    {
        var clock = new Clock(Evening);
        var cooldown = Cooldown(clock);

        cooldown.Refused("A");
        clock.Now += TimeSpan.FromMinutes(5);

        Assert.False(cooldown.IsResting("A"));
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

    [Fact]
    public void Refused_ManyTimes_NeverRestsLongerThanTheCeiling()
    {
        var cooldown = Cooldown(new Clock(Evening), ceiling: TimeSpan.FromMinutes(12));

        var rests = Enumerable.Range(0, 40).Select(_ => cooldown.Refused("A")).ToArray();

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

        Assert.False(cooldown.IsResting("A"));
        Assert.Equal(TimeSpan.FromMinutes(5), cooldown.Refused("A"));
    }

    [Fact]
    public void Refused_OneProvider_LeavesTheOthersAlone()
    {
        var cooldown = Cooldown(new Clock(Evening));

        cooldown.Refused("A");

        Assert.False(cooldown.IsResting("B"));
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
