namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers the reading every caller that drives a browser shares: the gap a site asked to be left alone for.
/// </summary>
/// <remarks>
/// The reading itself was never in doubt — the page reader had it from the start. What this file is for is that
/// there is now ONE of it: the errand and the call to a site's own service read the header through the same
/// function, and a test here holds all three.
/// </remarks>
public class RestsTests
{
    private static readonly DateTimeOffset Evening = new(2026, 9, 20, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AskedFor_SecondsAsARateLimiterSendsThem_IsThatManySeconds()
        => Assert.Equal(TimeSpan.FromSeconds(30), Rests.AskedFor(Headers("30"), Evening));

    [Fact]
    public void AskedFor_ADateAsACacheSendsIt_IsTheGapUntilThen()
        => Assert.Equal(TimeSpan.FromMinutes(5), Rests.AskedFor(Headers("Sun, 20 Sep 2026 20:05:00 GMT"), Evening));

    /// <summary>A date already past is no wait, not a negative one — which is what a clock a little out gives.</summary>
    [Fact]
    public void AskedFor_ADateAlreadyPast_IsNoGapAtAll()
        => Assert.Null(Rests.AskedFor(Headers("Sun, 20 Sep 2026 19:55:00 GMT"), Evening));

    [Fact]
    public void AskedFor_ZeroSeconds_IsNoGapAtAll()
        => Assert.Null(Rests.AskedFor(Headers("0"), Evening));

    [Fact]
    public void AskedFor_SomethingThatIsNeither_IsNoGapAtAll()
        => Assert.Null(Rests.AskedFor(Headers("soon"), Evening));

    [Fact]
    public void AskedFor_AnAnswerWithoutTheHeader_IsNoGapAtAll()
        => Assert.Null(Rests.AskedFor(new Dictionary<string, string> { ["content-type"] = "text/html" }, Evening));

    [Fact]
    public void AskedFor_NoAnswerAtAll_IsNoGapAtAll()
        => Assert.Null(Rests.AskedFor(headers: null, Evening));

    private static IReadOnlyDictionary<string, string> Headers(string asked)
        => new Dictionary<string, string> { ["retry-after"] = asked };
}
