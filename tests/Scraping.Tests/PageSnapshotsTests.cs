using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Scraping.Behavior;
using Snail.Toolkit.Web.Scraping.Extensions;
using Snail.Toolkit.Web.Tests.Browsers;
using Xunit.Abstractions;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers a picture of a page: what it shows, how much of the page it takes in, and what it refuses.
/// </summary>
[Collection(SiteCollection.Name)]
[Trait("Category", "Integration")]
public class PageSnapshotsTests(SiteFixture site, ITestOutputHelper output)
{
    [Fact]
    public async Task TakeAsync_RenderedPage_ReturnsAJpegOfTheScreen()
    {
        await using var host = Host();

        var outcome = await host.Snapshots.TakeAsync(Link.Of($"{SiteFixture.BaseUrl}/tall.html"), CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Equal([0xFF, 0xD8, 0xFF], outcome.Value.Image[..3].ToArray());
        Assert.Equal("Tall", outcome.Value.Title);
        Assert.Equal(200, outcome.Value.StatusCode);
        Assert.InRange(outcome.Value.Height, 1, 2999);
    }

    /// <summary>A picture that stops without saying so reads as a page that ended.</summary>
    [Fact]
    public async Task TakeAsync_ScreenOfAPageThatRunsOnBelowIt_SaysThePictureWasCut()
    {
        await using var host = Host();

        var outcome = await host.Snapshots.TakeAsync(Link.Of($"{SiteFixture.BaseUrl}/tall.html"), SnapshotFrame.Screen);

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.True(outcome.Value.IsCut);
    }

    [Fact]
    public async Task TakeAsync_WholePage_TakesInWhatIsBelowTheScreen()
    {
        await using var host = Host();

        var outcome = await host.Snapshots.TakeAsync(Link.Of($"{SiteFixture.BaseUrl}/tall.html"), SnapshotFrame.WholePage);

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Equal(3000, outcome.Value.Height);
        Assert.False(outcome.Value.IsCut);
    }

    /// <summary>
    /// A page is as long as its author likes; a picture of all of an endless one is megabytes nobody can
    /// look at. The ceiling holds, and the picture says it was reached.
    /// </summary>
    [Fact]
    public async Task TakeAsync_WholePageTallerThanTheCeiling_StopsAtItAndSaysSo()
    {
        await using var host = Host();

        var outcome = await host.Snapshots.TakeAsync(Link.Of($"{SiteFixture.BaseUrl}/endless.html"), SnapshotFrame.WholePage);

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Equal(PageSnapshots.TallestPage, outcome.Value.Height);
        Assert.True(outcome.Value.IsCut);
    }

    /// <summary>A site that would refuse the text refuses the picture: it is the same visit.</summary>
    [Fact]
    public async Task TakeAsync_ChallengePage_ReportsBlockedRatherThanAPictureOfTheWall()
    {
        await using var host = Host();

        var outcome = await host.Snapshots.TakeAsync(Link.Of($"{SiteFixture.BaseUrl}/challenge.html"), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Blocked, outcome.Failure.Kind);
    }

    [Fact]
    public async Task TakeAsync_MissingPage_ReportsTheStatusRatherThanAPictureOfTheErrorPage()
    {
        await using var host = Host();

        var outcome = await host.Snapshots.TakeAsync(Link.Of($"{SiteFixture.BaseUrl}/nowhere.html"), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Navigation, outcome.Failure.Kind);
        Assert.Equal(404, outcome.Failure.StatusCode);
    }

    private Harness Host()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddXUnit(output).SetMinimumLevel(LogLevel.Debug));
        services.AddScraping(options =>
        {
            options.BrowserServer = site.BrowserServer;
            options.Engine = site.Engine;
            options.MaxRetryAttempts = 0;
            options.NavigationTimeoutMs = 20_000;
        });

        services.MatchingFingerprint();

        services.Configure<HumanBehaviorOptions>(options => options.Enabled = false);
        services.Configure<PolitenessOptions>(options => options.MinDelayPerHost = TimeSpan.Zero);

        return new Harness(services.BuildServiceProvider());
    }

    private sealed class Harness(ServiceProvider provider) : IAsyncDisposable
    {
        public IPageSnapshots Snapshots { get; } = provider.GetRequiredService<IPageSnapshots>();

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
