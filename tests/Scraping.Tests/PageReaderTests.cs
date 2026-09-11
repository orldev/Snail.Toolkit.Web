using System.Text.Json;
using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Scraping.Behavior;
using Snail.Toolkit.Web.Scraping.Extensions;
using Snail.Toolkit.Web.Text;
using Xunit.Abstractions;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Drives the real reader against a real browser and a fixture site.
/// </summary>
/// <remarks>Requires Docker. Filter with <c>--filter Category!=Integration</c> to skip.</remarks>
[Collection(SiteCollection.Name)]
[Trait("Category", "Integration")]
public class PageReaderTests(SiteFixture site, ITestOutputHelper output)
{
    [Fact]
    public async Task ReadAsync_RenderedPage_ReturnsItsMarkup()
    {
        await using var host = Host();

        var outcome = await host.Reader.ReadAsync(Link.Of($"{SiteFixture.BaseUrl}/article.html"));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("Grain report", outcome.Value.Html, StringComparison.Ordinal);
        Assert.Equal(200, outcome.Value.StatusCode);
        Assert.False(outcome.Value.IsRedirected);
    }

    [Fact]
    public async Task ReadAsync_ChallengePage_ReportsBlockedRatherThanEmpty()
    {
        await using var host = Host();

        var outcome = await host.Reader.ReadAsync(Link.Of($"{SiteFixture.BaseUrl}/challenge.html"));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Blocked, outcome.Failure.Kind);
        Assert.Contains("cf-browser-verification", outcome.Failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An empty document is the site's answer, not a failure of the client, and says so.</summary>
    [Fact]
    public async Task ReadAsync_PageWithoutContent_ReportsEmpty()
    {
        await using var host = Host();

        var outcome = await host.Reader.ReadAsync(Link.Of($"{SiteFixture.BaseUrl}/blank.html"));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Empty, outcome.Failure.Kind);
        Assert.False(outcome.Failure.IsTransient);
    }

    /// <summary>An error page is not the requested page, and a 404 is not worth retrying.</summary>
    [Fact]
    public async Task ReadAsync_MissingPage_ReportsANonTransientNavigationFailure()
    {
        await using var host = Host();

        var outcome = await host.Reader.ReadAsync(Link.Of($"{SiteFixture.BaseUrl}/nowhere.html"));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Navigation, outcome.Failure.Kind);
        Assert.Equal(404, outcome.Failure.StatusCode);
        Assert.False(outcome.Failure.IsTransient);
    }

    /// <summary>
    /// <c>navigator.webdriver</c> must read <c>false</c>, not vanish.
    /// </summary>
    /// <remarks>
    /// Chrome has carried this property on <c>Navigator.prototype</c> since version 88, so a browser where it is
    /// missing is as unusual as one where it is true. The patch overwrites it on the prototype that owns it
    /// rather than adding an own property to the instance, which is itself a detectable difference.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_WebdriverFlag_ReadsFalseRatherThanMissing()
    {
        await using var host = Host();

        var probe = await ProbeAsync(host);

        Assert.Equal("false", probe.GetProperty("webdriver").GetString());
    }

    [Fact]
    public async Task ReadAsync_UnresolvableHost_ReportsNavigationFailure()
    {
        await using var host = Host();

        var outcome = await host.Reader.ReadAsync(Link.Of("http://no-such-host.invalid/"));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Navigation, outcome.Failure.Kind);
        Assert.True(outcome.Failure.IsTransient);
    }

    /// <summary>An address that is not an absolute http one is a failure, never an exception.</summary>
    [Fact]
    public async Task ReadAsync_SomethingThatIsNotAnAddress_ReportsNavigationFailure()
    {
        await using var host = Host();

        var outcome = await host.Reader.ReadAsync(Link.Of("mailto:someone@example.com"));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Navigation, outcome.Failure.Kind);
    }

    /// <summary>The injected script must report the identity the context was opened with.</summary>
    [Fact]
    public async Task ReadAsync_InjectedScript_MatchesTheContextIdentity()
    {
        await using var host = Host(identity => identity.Fingerprints =
        [
            new Fingerprint
            {
                UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                Locale = "de-DE",
                Timezone = "Europe/Berlin",
                AcceptLanguage = "de-DE,de;q=0.9,en;q=0.8"
            }
        ]);

        var probe = await ProbeAsync(host);

        Assert.Equal("MacIntel", probe.GetProperty("platform").GetString());
        Assert.Equal(
            ["de-DE", "de", "en"],
            probe.GetProperty("languages").EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>
    /// <c>navigator.userAgentData</c> has to name the brand the <c>Sec-Ch-Ua</c> header already claims.
    /// </summary>
    /// <remarks>
    /// Measured against a live site on 2026-09-11: the context sent <c>Sec-Ch-Ua</c> for Chrome while the page
    /// still read <c>HeadlessChrome;151</c> from <c>navigator.userAgentData</c>, and the site's analytics carried
    /// that build away in its <c>uafvl</c> parameter. The probe is served over TLS here because
    /// <c>userAgentData</c> exists only in a secure context: over plain http it reads as absent, and an assertion
    /// against it would pass while proving nothing.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ClientHints_NameTheBrandTheHeadersClaim()
    {
        await using var host = Host(
            identity => identity.Fingerprints =
            [
                new Fingerprint
                {
                    UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                    Locale = "en-GB",
                    Timezone = "Europe/London",
                    AcceptLanguage = "en-GB,en;q=0.9"
                }
            ],
            options => options.IgnoreCertificateErrors = true);

        var probe = await ProbeAsync(host, SiteFixture.SecureBaseUrl);

        var brands = probe.GetProperty("brands").GetString();

        Assert.DoesNotContain("Headless", brands, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Google Chrome;124", brands, StringComparison.Ordinal);
        Assert.Equal("macOS", probe.GetProperty("uaPlatform").GetString());
    }

    /// <summary>
    /// Regression: the spoofed <c>getParameter</c> called the original without its receiver, so every query
    /// other than the two spoofed ones threw <c>Illegal invocation</c> and broke WebGL outright.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WebGlQueries_SurviveTheSpoofedParameters()
    {
        await using var host = Host();

        var probe = await ProbeAsync(host);

        var webgl = probe.GetProperty("webgl").GetString();
        var vendor = probe.GetProperty("vendor").GetString();

        Assert.False(webgl?.StartsWith("threw:", StringComparison.Ordinal), webgl);
        Assert.Equal("Intel Inc.", vendor);
    }

    /// <summary>
    /// The budget neither engine had: a five-megabyte page used to reach an agent's context window whole, and a
    /// cut with no marker is worse than no cut at all.
    /// </summary>
    [Fact]
    public async Task ReadAsync_TextOverTheBudget_ComesBackCutAndSaysSo()
    {
        await using var host = Host(tune: options => options.MaxContentCharacters = 20);

        var outcome = await host.Reader.ReadAsync(Link.Of($"{SiteFixture.BaseUrl}/article.html"));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.True(outcome.Value.IsTruncated);
        Assert.Contains(ContentBudget.Marker, outcome.Value.Text, StringComparison.Ordinal);
        Assert.True(outcome.Value.OriginalLength > 20);
    }

    /// <summary>
    /// The budget used to cut the markup by character, which ended the document mid-tag and lost every link
    /// past the cut — the failure a browser-backed search engine cannot see and cannot recover from.
    /// </summary>
    [Fact]
    public async Task ReadAsync_TextOverTheBudget_LeavesTheMarkupWhole()
    {
        await using var host = Host(tune: options => options.MaxContentCharacters = 20);

        var outcome = await host.Reader.ReadAsync(Link.Of($"{SiteFixture.BaseUrl}/article.html"));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("</html>", outcome.Value.Html, StringComparison.Ordinal);
        Assert.DoesNotContain(ContentBudget.Marker, outcome.Value.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_MixedAddresses_ReportsAnOutcomePerAddress()
    {
        await using var host = Host();

        var results = await host.Crawler.ReadAllAsync(
        [
            Link.Of($"{SiteFixture.BaseUrl}/article.html"),
            Link.Of($"{SiteFixture.BaseUrl}/challenge.html"),
            Link.Of("http://no-such-host.invalid/")
        ]);

        Assert.Equal(3, results.Count);
        Assert.Single(results, outcome => outcome.IsSuccess);
        Assert.Single(results, outcome => outcome.Failure?.Kind is FailureKind.Blocked);
        Assert.Single(results, outcome => outcome.Failure?.Kind is FailureKind.Navigation);
    }

    /// <summary>
    /// The reader extracts the text itself. It used to hand back markup alone, so every caller cleaned it
    /// again — and an agent that skipped the step read a page of tags.
    /// </summary>
    [Fact]
    public async Task ReadAsync_RenderedArticle_ExtractsTextWithoutScriptsOrStyles()
    {
        await using var host = Host();

        var outcome = await host.Reader.ReadAsync(Link.Of($"{SiteFixture.BaseUrl}/article.html"));
        var text = outcome.Value.Text;

        Assert.DoesNotContain("window.tracked", text, StringComparison.Ordinal);
        Assert.DoesNotContain("color: red", text, StringComparison.Ordinal);
        Assert.Contains("Grain report\nWheat prices rose.\nBarley held steady.", text, StringComparison.Ordinal);
    }

    private async Task<JsonElement> ProbeAsync(Harness host, string origin = SiteFixture.BaseUrl)
    {
        var outcome = await host.Reader.ReadAsync(Link.Of($"{origin}/probe.html"));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());

        output.WriteLine(outcome.Value.Text);

        return JsonDocument.Parse(outcome.Value.Text).RootElement;
    }

    private Harness Host(Action<IdentityOptions>? identity = null, Action<ScrapingOptions>? tune = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddXUnit(output).SetMinimumLevel(LogLevel.Debug));
        services.AddScraping(options =>
        {
            options.BrowserServer = site.BrowserServer;
            options.MaxRetryAttempts = 0;
            options.NavigationTimeoutMs = 20_000;
            tune?.Invoke(options);
        });

        services.Configure<HumanBehaviorOptions>(options => options.Enabled = false);
        services.Configure<PolitenessOptions>(options => options.MinDelayPerHost = TimeSpan.Zero);

        if (identity is not null)
            services.Configure(identity);

        return new Harness(services.BuildServiceProvider());
    }

    private sealed class Harness(ServiceProvider provider) : IAsyncDisposable
    {
        public IPageReader Reader { get; } = provider.GetRequiredService<IPageReader>();

        public ICrawler Crawler { get; } = provider.GetRequiredService<ICrawler>();

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
