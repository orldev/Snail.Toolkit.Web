namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers what a caller branches on: which failures are worth another attempt, and what counts as a redirect.
/// </summary>
public class PageOutcomeTests
{
    [Theory]
    [InlineData(FailureKind.Blocked)]
    [InlineData(FailureKind.Timeout)]
    [InlineData(FailureKind.BrowserUnavailable)]
    [InlineData(FailureKind.Proxy)]
    public void IsTransient_FailuresAnotherIdentityCouldSurvive_AsksForARetry(FailureKind kind)
        => Assert.True(new PageFailure(kind, "x").IsTransient);

    /// <summary>
    /// Retrying these only repeats the same work: the site has already given its final answer, and three
    /// attempts with exponential backoff spend minutes to be told the same thing.
    /// </summary>
    [Theory]
    [InlineData(FailureKind.RobotsDisallowed)]
    [InlineData(FailureKind.Empty)]
    [InlineData(FailureKind.Unexpected)]
    public void IsTransient_FailuresThatAreTheSitesFinalAnswer_AskForNothing(FailureKind kind)
        => Assert.False(new PageFailure(kind, "x").IsTransient);

    [Theory]
    [InlineData(404, false)]
    [InlineData(403, false)]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(null, true)]
    public void IsTransient_NavigationFailure_FollowsTheStatus(int? status, bool isTransient)
        => Assert.Equal(isTransient, new PageFailure(FailureKind.Navigation, "x") { StatusCode = status }.IsTransient);

    /// <summary>
    /// A redirect is judged on canonical addresses. Comparing the raw strings called an http address answered
    /// over https a redirect, which is the single most common shape of a request that was not redirected at all.
    /// </summary>
    [Fact]
    public void IsRedirected_SameLinkInAnotherShape_ReportsNoRedirect()
    {
        var page = new PageContent(Link.Of("http://example.com/a"), "<html/>", string.Empty, DateTimeOffset.UtcNow)
        {
            Final = Link.Of("https://example.com/a/")
        };

        Assert.False(page.IsRedirected);
    }

    [Fact]
    public void IsRedirected_AConsentWall_ReportsARedirect()
    {
        var page = new PageContent(Link.Of("https://example.com/a"), "<html/>", string.Empty, DateTimeOffset.UtcNow)
        {
            Final = Link.Of("https://consent.example.com/?continue=x")
        };

        Assert.True(page.IsRedirected);
    }

    [Fact]
    public void ToString_FailureWithAStatus_NamesIt()
        => Assert.Equal(
            "Navigation (503): unreachable",
            new PageFailure(FailureKind.Navigation, "unreachable") { StatusCode = 503 }.ToString());
}
