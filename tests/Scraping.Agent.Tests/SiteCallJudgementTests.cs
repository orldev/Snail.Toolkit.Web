using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping.Agent.Tests;

/// <summary>
/// Covers what a service's answer is read as, without a browser between the test and the reading.
/// </summary>
/// <remarks>
/// The call itself needs a page to be made from, and its judgement does not. Read on 2026-09-20: a service
/// refusing by frequency in the middle of an errand carried its status and never the gap it named, so the
/// per-host gate — which is what makes such a gap bind every later caller instead of this one — had nothing to
/// write. That is the twelfth item on the consumer's list.
/// </remarks>
public class SiteCallJudgementTests
{
    private static readonly DateTimeOffset Evening = new(2026, 9, 20, 20, 0, 0, TimeSpan.Zero);

    private static readonly SiteCall Call = new(Link.Of("https://registry.example/search"), "/api/find", "{}");

    [Fact]
    public void Judge_ServiceRefusingByFrequency_CarriesTheGapItAskedFor()
    {
        var outcome = SiteCalls.Judge(Call, Identity, Answered(429, "too many requests", retryAfter: "30"), Evening);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(TimeSpan.FromSeconds(30), outcome.Failure!.RetryAfter);
        Assert.Equal(429, outcome.Failure!.StatusCode);
        Assert.True(outcome.Failure!.IsRefusal);
    }

    /// <summary>A date is the other form the header comes in, and a service under a cache sends that one.</summary>
    [Fact]
    public void Judge_GapNamedAsADate_CarriesTheTimeUntilThen()
    {
        var answered = Answered(503, "maintenance", retryAfter: "Sun, 20 Sep 2026 20:05:00 GMT");

        Assert.Equal(TimeSpan.FromMinutes(5), SiteCalls.Judge(Call, Identity, answered, Evening).Failure!.RetryAfter);
    }

    /// <summary>A service that named no gap is held to nothing beyond the configured pace.</summary>
    [Fact]
    public void Judge_RefusalWithoutAGap_CarriesNone()
        => Assert.Null(SiteCalls.Judge(Call, Identity, Answered(500, "boom"), Evening).Failure!.RetryAfter);

    /// <summary>An answer is still an answer: the body comes back and nothing is read as a refusal.</summary>
    [Fact]
    public void Judge_ServiceThatAnswered_ReturnsItsBody()
    {
        var outcome = SiteCalls.Judge(Call, Identity, Answered(200, "{\"found\":1}"), Evening);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("{\"found\":1}", outcome.Value);
    }

    /// <summary>An empty body is the service's final answer rather than a gap to wait out.</summary>
    [Fact]
    public void Judge_ServiceThatAnsweredNothing_IsAnEmptyFailure()
        => Assert.Equal(FailureKind.Empty, SiteCalls.Judge(Call, Identity, Answered(200, " "), Evening).Failure!.Kind);

    private static string Answered(int status, string body, string? retryAfter = null)
        => System.Text.Json.JsonSerializer.Serialize(new { status, retryAfter, body });

    private static WebIdentity Identity => new()
    {
        UserAgent = "Agent/1.0",
        Platform = "Windows",
        NavigatorPlatform = "Win32",
        Brand = "Google Chrome",
        MajorVersion = "124",
        Locale = "en-US",
        Timezone = "UTC",
        AcceptLanguage = "en-US,en;q=0.9",
        Languages = ["en-US", "en"]
    };
}
