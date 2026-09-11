using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Scraping.Agent.Extensions;
using Snail.Toolkit.Web.Scraping.Behavior;
using Xunit.Abstractions;

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

    private Harness Host()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddXUnit(output).SetMinimumLevel(LogLevel.Debug));
        services.AddPageAgent(options =>
        {
            options.BrowserServer = site.BrowserServer;
            options.MaxRetryAttempts = 0;
            options.NavigationTimeoutMs = 20_000;
        });

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
