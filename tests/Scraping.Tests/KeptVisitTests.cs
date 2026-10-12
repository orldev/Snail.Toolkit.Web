using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Scraping.Behavior;
using Snail.Toolkit.Web.Scraping.Browsers;
using Snail.Toolkit.Web.Scraping.Extensions;
using Snail.Toolkit.Web.Tests.Browsers;
using Xunit.Abstractions;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Drives a real browser to show that what a site leaves survives the visit that earned it.
/// </summary>
/// <remarks>
/// The point of the whole memory, stated as the site would state it: a visitor who was let in once is let in
/// again. Written against a cookie because that is what a cleared challenge is — Cloudflare's
/// <c>cf_clearance</c>, a consent wall's preference, a registry's session. Requires Docker.
/// <para>
/// THE OTHER HALF — that a cookie is not handed to another site — is held by <c>KeepsakesTests</c> and not
/// here, and the reason is the fixture: it serves ONE host, and the address by which the test process sees
/// that server is not reachable from the browser's container. A check written against it would prove the
/// container's networking rather than this library's memory, and the first draft of it did exactly that:
/// «net::ERR_CONNECTION_REFUSED at 127.0.0.1».
/// </para>
/// </remarks>
[Collection(SiteCollection.Name)]
[Trait("Category", "Integration")]
public class KeptVisitTests(SiteFixture site, ITestOutputHelper output)
{
    [ChromiumFact]
    public async Task OpenAsync_CookieLeftByOneVisit_IsThereOnTheNext()
    {
        await using var host = Host();

        await Visited(host, SiteFixture.BaseUrl, page => page.EvaluateAsync<string>(
            "() => { document.cookie = 'clearance=granted; path=/'; return document.cookie; }"));

        var seen = await Visited(host, SiteFixture.BaseUrl, page => page.EvaluateAsync<string>("() => document.cookie"));

        Assert.Contains("clearance=granted", seen, StringComparison.Ordinal);
    }

    private static async Task<string> Visited(Harness host, string root, Func<IPage, Task<string>> ask)
    {
        var target = new Uri($"{root}/grain.html");
        var identity = host.Provider.GetRequiredService<IIdentityRotation>().For(target.Host);

        await using var visit = await host.Provider.GetRequiredService<IVisits>()
            .OpenAsync(identity, Appetite.Everything, target);

        await visit.Page.GotoAsync(target.ToString());

        return await ask(visit.Page);
    }

    private Harness Host()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddXUnit(output).SetMinimumLevel(LogLevel.Warning));
        services.AddScraping(options =>
        {
            options.BrowserServer = site.BrowserServer;
            options.Engine = site.Engine;
            options.MaxRetryAttempts = 0;
            options.NavigationTimeoutMs = 20_000;
            options.IgnoreCertificateErrors = true;
        });

        services.MatchingFingerprint();
        services.Configure<HumanBehaviorOptions>(options => options.Enabled = false);
        services.Configure<PolitenessOptions>(options => options.MinDelayPerHost = TimeSpan.Zero);

        return new Harness(services.BuildServiceProvider());
    }

    private sealed class Harness(ServiceProvider provider) : IAsyncDisposable
    {
        public ServiceProvider Provider { get; } = provider;

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }
}
