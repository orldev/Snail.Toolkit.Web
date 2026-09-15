using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Scraping.Agent.Extensions;
using Snail.Toolkit.Web.Scraping.Behavior;
using Xunit.Abstractions;
using Snail.Toolkit.Web.Tests.Browsers;

namespace Snail.Toolkit.Web.Scraping.Agent.Tests;

/// <summary>
/// Drives the real caller against a real browser and a service that only answers its own page.
/// </summary>
/// <remarks>Requires Docker. Filter with <c>--filter Category!=Integration</c> to skip.</remarks>
[Collection(FormCollection.Name)]
[Trait("Category", "Integration")]
public class SiteCallsTests(FormFixture site, ITestOutputHelper output)
{
    [Fact]
    public async Task SendAsync_ServiceThatAnswers_ReturnsItsBodyRatherThanThePage()
    {
        await using var host = Host();

        var outcome = await host.Calls.SendAsync(new SiteCall(
            Link.Of($"{FormFixture.BaseUrl}/minting.html"), "/suggest.json", string.Empty) { Method = "GET" });

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("Acme Holdings", outcome.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("service desk", outcome.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// The credential has to reach the service, under the action it was minted for.
    /// </summary>
    /// <remarks>
    /// Measured on a registry on 2026-09-11: it mints a separate token per route — <c>search_fedresurs</c> for
    /// one, <c>search_notary</c> for the other — and validates the action against the route, so a token minted
    /// under a guessed action is refused although it minted perfectly well. That cost an hour of blaming the
    /// browser, and is why the action travels with the request here rather than being assumed.
    /// </remarks>
    [Fact]
    public async Task SendAsync_ServiceThatWantsACredential_SendsTheOneMintedForThatAction()
    {
        await using var host = Host();

        var outcome = await host.Calls.SendAsync(new SiteCall(
            Link.Of($"{FormFixture.BaseUrl}/minting.html"), "/echo", string.Empty)
        {
            Method = "GET",
            Mint = new Mint("site-key", "search_notary")
        });

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("token=minted-search_notary", outcome.Value, StringComparison.Ordinal);
    }

    /// <summary>A page that cannot mint says so, instead of calling the service without a credential.</summary>
    [Fact]
    public async Task SendAsync_PageWithoutAMintingScript_NamesTheCredentialItCouldNotIssue()
    {
        await using var host = Host();

        var outcome = await host.Calls.SendAsync(new SiteCall(
            Link.Of($"{FormFixture.BaseUrl}/form.html"), "/echo", string.Empty)
        {
            Method = "GET",
            Mint = new Mint("site-key", "search_fedresurs") { Within = TimeSpan.FromSeconds(3) }
        });

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Blocked, outcome.Failure.Kind);
        Assert.Contains("search_fedresurs", outcome.Failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A service that is not there is a navigation failure, not an empty answer.</summary>
    [Fact]
    public async Task SendAsync_ServiceThatIsNotThere_ReportsTheStatusRatherThanNothing()
    {
        await using var host = Host();

        var outcome = await host.Calls.SendAsync(new SiteCall(
            Link.Of($"{FormFixture.BaseUrl}/minting.html"), "/nowhere.json", string.Empty) { Method = "GET" });

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Navigation, outcome.Failure.Kind);
        Assert.Equal(404, outcome.Failure.StatusCode);
    }

    /// <summary>
    /// A service that keeps answering «busy» is asked again, and the wait between attempts proves it.
    /// </summary>
    /// <remarks>
    /// There is no counting endpoint to ask how many times the call arrived, so the retry is measured by the
    /// one thing it spends: time. A single attempt against this fixture costs tens of milliseconds and the
    /// first backoff is set to four hundred, so an answer that took at least that long cannot have come from
    /// one attempt. The failure itself must still be reported faithfully once the attempts run out — a retry
    /// that swallowed the status would be worse than no retry at all.
    /// </remarks>
    [Fact]
    public async Task SendAsync_AServiceThatAnswersBusy_IsAskedAgainBeforeGivingUp()
    {
        await using var host = Host(options =>
        {
            options.MaxRetryAttempts = 2;
            options.RetryDelay = TimeSpan.FromMilliseconds(400);
        });

        var started = Stopwatch.GetTimestamp();

        var outcome = await host.Calls.SendAsync(new SiteCall(
            Link.Of($"{FormFixture.BaseUrl}/minting.html"), "/flaky", string.Empty) { Method = "GET" });

        var spent = Stopwatch.GetElapsedTime(started);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(503, outcome.Failure.StatusCode);
        Assert.True(spent >= TimeSpan.FromMilliseconds(400), $"the call took {spent} and cannot have been retried");
    }

    /// <summary>
    /// A service that is not there is not asked twice, however generous the retry settings are.
    /// </summary>
    /// <remarks>
    /// The delay is five seconds precisely so that a retry would be unmistakable: a 404 that was tried again
    /// could not come back in less. This is the half of the rule that costs time when it is wrong — retrying
    /// a fact spends minutes to be told the same thing.
    /// </remarks>
    [Fact]
    public async Task SendAsync_AServiceThatIsNotThere_IsNotAskedAgain()
    {
        await using var host = Host(options =>
        {
            options.MaxRetryAttempts = 3;
            options.RetryDelay = TimeSpan.FromSeconds(5);
        });

        var started = Stopwatch.GetTimestamp();

        var outcome = await host.Calls.SendAsync(new SiteCall(
            Link.Of($"{FormFixture.BaseUrl}/minting.html"), "/nowhere.json", string.Empty) { Method = "GET" });

        var spent = Stopwatch.GetElapsedTime(started);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(404, outcome.Failure.StatusCode);
        Assert.True(spent < TimeSpan.FromSeconds(5), $"the call took {spent} and was retried after a final answer");
    }

    private Harness Host(Action<ScrapingOptions>? tune = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddXUnit(output).SetMinimumLevel(LogLevel.Debug));
        services.AddPageAgent(options =>
        {
            options.BrowserServer = site.BrowserServer;
            options.Engine = site.Engine;
            options.MaxRetryAttempts = 0;
            options.NavigationTimeoutMs = 20_000;
            tune?.Invoke(options);
        });

        services.MatchingFingerprint();

        services.Configure<HumanBehaviorOptions>(options => options.Enabled = false);
        services.Configure<PolitenessOptions>(options => options.MinDelayPerHost = TimeSpan.Zero);

        return new Harness(services.BuildServiceProvider());
    }

    private sealed class Harness(ServiceProvider provider) : IAsyncDisposable
    {
        public ISiteCalls Calls { get; } = provider.GetRequiredService<ISiteCalls>();

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
