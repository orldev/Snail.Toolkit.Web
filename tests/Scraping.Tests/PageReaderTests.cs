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

using Snail.Toolkit.Web.Tests.Browsers;

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

    /// <summary>Reading a page fetches what it asks for, the way a person's browser would.</summary>
    /// <remarks>
    /// The pair of these two is the point: the same page, the same reader, one axis. Without the first, the
    /// second would pass against a browser that had quietly stopped fetching anything at all.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_PageWithASubresource_FetchesIt()
    {
        await using var host = Host();

        var outcome = await host.Reader.ReadAsync(Link.Of($"{SiteFixture.BaseUrl}/tracked.html"));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("<title>tracked</title>", outcome.Value.Html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asked for the document alone, the browser refuses every subresource the page goes on to ask for.
    /// </summary>
    /// <remarks>
    /// Measured against Brave on 2026-09-14: a site that counts requests rather than pages reads one ordinary
    /// page load as a burst of seventy-odd, and answered <c>429</c> three times out of three where the document
    /// alone was answered <c>200</c> three times out of three.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_DocumentAlone_RefusesTheSubresource()
    {
        await using var host = Host();

        var outcome = await host.Reader.ReadAsync(
            Link.Of($"{SiteFixture.BaseUrl}/tracked.html"),
            Browsers.Appetite.DocumentOnly);

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("<title>quiet</title>", outcome.Value.Html, StringComparison.Ordinal);
        Assert.Contains("Wheat prices rose", outcome.Value.Html, StringComparison.Ordinal);
    }

    /// <summary>A site refusing by frequency says how long to wait, and the failure carries the number.</summary>
    /// <remarks>
    /// Until 2026-09-14 nothing in the toolkit read <c>Retry-After</c> at all, so being told "too often" was
    /// answered with three more requests on a schedule of our own. The header is served here in its seconds
    /// form, which is the one a rate limiter uses; the date form is parsed too and is what a cache emits.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_SiteAskingToBeLeftAlone_CarriesTheGapItAskedFor()
    {
        await using var host = Host();

        var outcome = await host.Reader.ReadAsync(Link.Of($"{SiteFixture.BaseUrl}/busy.html"));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(429, outcome.Failure.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(3), outcome.Failure.RetryAfter);
    }

    /// <summary>
    /// A site asking for longer than the toolkit will wait is believed at once, and costs one request.
    /// </summary>
    /// <remarks>
    /// The whole point of the change this guards, measured end to end on 2026-09-14. Before it, this page —
    /// which asks for thirty seconds — was read with retries on and cost the caller its entire budget in the
    /// gate, ending in a cancellation that named neither the status nor the number the site had already sent
    /// in its first answer. Retries are deliberately left ON here: with them off the check would pass against
    /// any build at all.
    /// <para>
    /// The requests are counted at the site rather than at the reader, because "one refusal cost four
    /// requests" is a fact about what reached the site, and the reader is the party whose count is in doubt.
    /// </para>
    /// <para>
    /// The assertions name the failure they saw rather than only its status. This one went red once in five
    /// full runs of the solution on 2026-09-14 — where six suites start their containers at the same time and
    /// never in a run of this suite alone — and the message said only which number differed, so which of a
    /// contended navigation and a miscounted request it was could not be told afterwards. It has not been
    /// reproduced; what changed here is only that the next occurrence will say what came back.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadAsync_SiteAskingForLongerThanWeWait_AnswersAtOnceAndAsksOnce()
    {
        var since = DateTime.UtcNow;

        await using var host = Host(tune: options => options.MaxRetryAttempts = 3);

        var started = DateTimeOffset.UtcNow;
        var outcome = await host.Reader.ReadAsync(Link.Of($"{SiteFixture.BaseUrl}/busy-for-ages.html"));
        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.False(outcome.IsSuccess);
        Assert.True(outcome.Failure.StatusCode is 429, $"the site answered {outcome.Failure}");
        Assert.Equal(TimeSpan.FromSeconds(30), outcome.Failure.RetryAfter);
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"{elapsed} for {outcome.Failure}");
        Assert.Equal(1, await site.TimesAskedForAsync("/busy-for-ages.html", since));
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
    /// <remarks>
    /// Chromium only because the identity it pins is a Chrome one, and registration refuses a Chrome
    /// fingerprint on any other engine — a Gecko browser carrying a Chrome user agent is the contradiction
    /// that check exists to catch. Run against a Firefox fixture this failed before the browser even started,
    /// which reads as a broken test rather than as a suite pointed at the wrong engine.
    /// </remarks>
    [ChromiumFact]
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
    [ChromiumFact]
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
    /// <remarks>
    /// Chromium only: the vendor it expects is the one the spoof writes over a Chromium renderer, and on
    /// another engine the check would be asserting somebody else's string.
    /// </remarks>
    [ChromiumFact]
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
            options.Engine = site.Engine;
            options.MaxRetryAttempts = 0;
            options.NavigationTimeoutMs = 20_000;
            tune?.Invoke(options);
        });

        services.MatchingFingerprint();

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
