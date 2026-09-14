using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// Drives the doubling rest to the end of the type it is held in.
/// </summary>
/// <remarks>
/// <c>LengthOf</c> doubles the configured cooldown once per refusal while it is still under the ceiling, and
/// the ceiling is a caller's to choose. Start-up validation asks only that it be no shorter than the first
/// cooldown, so <see cref="TimeSpan.MaxValue"/> — which is how somebody writes "keep backing off, never give
/// up" — passes every check the host makes and then cannot be reached by doubling.
/// </remarks>
public class SearchCooldownCeilingTests
{
    /// <summary>A run of refusals reaches the ceiling rather than the end of the arithmetic.</summary>
    /// <remarks>
    /// The loop runs while <c>length &lt; MaxRefusalCooldown</c>, and with the ceiling at
    /// <see cref="TimeSpan.MaxValue"/> that condition is true right up to the multiplication that cannot be
    /// performed: five minutes doubled past forty-one times is more ticks than a <see cref="TimeSpan"/> holds,
    /// and <c>length *= 2</c> answers with <see cref="OverflowException"/>.
    /// <para>
    /// MEASURED on 2026-09-14: five minutes doubled sixty times throws. It leaves <c>Refused</c>, which is
    /// called from <c>WebSearch.Unanswered</c> while recording a refusal — so the engine that refused takes
    /// the search down instead of being rested, and the exception says nothing about either.
    /// </para>
    /// <para>
    /// Forty-two consecutive refusals of one provider is a long-running process doing exactly what the
    /// doubling is for: the rest grows, the provider keeps refusing, and the record of the run is kept across
    /// rests on purpose.
    /// </para>
    /// </remarks>
    [Fact]
    public void Refused_ManyTimesUnderACeilingNothingCanReach_RestsRatherThanThrows()
    {
        var cooldown = Cooldown(TimeSpan.FromMinutes(5), TimeSpan.MaxValue);

        for (var refusal = 0; refusal < 60; refusal++)
            cooldown.Refused("Bing");

        Assert.True(cooldown.IsResting("Bing"));
    }

    /// <summary>The rest never exceeds the ceiling, whatever the ceiling is.</summary>
    /// <remarks>
    /// The same defect stated as the answer rather than the throw: every rest this hands back has to be a
    /// value a caller can wait for and log. Checked one refusal at a time so the first one that cannot be
    /// computed is named by its number.
    /// </remarks>
    [Fact]
    public void Refused_EachTimeUnderAnUnreachableCeiling_AlwaysNamesALengthThatCanBeWaited()
    {
        var cooldown = Cooldown(TimeSpan.FromMinutes(5), TimeSpan.MaxValue);

        for (var refusal = 1; refusal <= 60; refusal++)
        {
            var rest = cooldown.Refused("Bing");

            Assert.True(rest > TimeSpan.Zero, $"refusal {refusal} rested for {rest}");
        }
    }

    private static SearchCooldown Cooldown(TimeSpan first, TimeSpan ceiling)
        => new(
            Options.Create(new SearchOptions { RefusalCooldown = first, MaxRefusalCooldown = ceiling }),
            TimeProvider.System);
}
