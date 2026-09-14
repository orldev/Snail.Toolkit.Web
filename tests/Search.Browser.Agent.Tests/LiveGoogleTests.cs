using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snail.Toolkit.Web.Search.Browser.Agent.Extensions;
using Snail.Toolkit.Web.Tests;

namespace Snail.Toolkit.Web.Search.Browser.Agent.Tests;

/// <summary>
/// Asks the live engine one query and checks the answer is still made of addresses.
/// </summary>
/// <remarks>
/// The rest of this suite drives a stubbed agent over fixture markup, which is what makes it hermetic and also
/// what leaves the whole point untested: that this engine answers a browser at all, and that the links it
/// answers with survive into addresses. Every defect this package was written for lived in that gap — the
/// results address answering <c>/sorry/</c> where the form answers results, the stealth script undoing the
/// build it runs on, and a page of wrapped links parsing into nothing.
/// <para>
/// The engine is asked ONCE and the answer read by every check below. A live request is the scarce thing, not
/// a test method: this engine is given a two-minute floor between queries, so five requests would be a
/// ten-minute run. Skipped unless <see cref="LiveFactAttribute.Variable"/> names a browser server — asking a
/// third party over the public internet is not something a suite should do because somebody typed
/// <c>dotnet test</c>.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Category", "Live")]
public class LiveGoogleTests
{
    private const string Query = "postgresql documentation";
    private const string Expected = "postgresql.org";
    private const string Engine = "google.com";

    private static readonly Lazy<Task<SearchAnswer>> Asked = new(AskAsync);

    [LiveFact]
    public async Task FindAsync_ARealQuery_IsAnsweredWithResults()
        => LiveAnswer.Of(await Asked.Value);

    /// <summary>
    /// Every result is somewhere a caller can go — which is the check this engine taught us to write.
    /// </summary>
    /// <remarks>
    /// Measured on 2026-09-14: every link on the page arrives as a relative <c>/goto</c> whose query is an
    /// encrypted blob, and the address behind it is known only by asking the engine where the link leads. When
    /// that resolution stops working the results do not disappear loudly; they arrive with no host at all and
    /// the outbound filter drops the lot, which reads as a quiet day.
    /// </remarks>
    [LiveFact]
    public async Task FindAsync_EveryResult_LeadsAwayFromTheEngine()
        => LiveAnswer.LeadsAway(LiveAnswer.Of(await Asked.Value), Engine);

    /// <summary>The one check that reads the world rather than the shape.</summary>
    /// <remarks>
    /// The official documentation is what this query is answered with. A count would pass on a page built
    /// entirely out of the engine's own furniture.
    /// </remarks>
    [LiveFact]
    public async Task FindAsync_TheAnswer_NamesTheSiteTheQueryIsAbout()
        => LiveAnswer.About(LiveAnswer.Of(await Asked.Value), Expected);

    /// <summary>A heading on at least one result: none at all is the layout change, one missing is a Tuesday.</summary>
    [LiveFact]
    public async Task FindAsync_AtLeastOneResult_CarriesAHeading()
        => LiveAnswer.Titled(LiveAnswer.Of(await Asked.Value));

    /// <summary>Same reasoning for the prose: this parser reads it from <c>data-sncf</c>, which is not ours.</summary>
    [LiveFact]
    public async Task FindAsync_AtLeastOneResult_CarriesADescription()
        => LiveAnswer.Described(LiveAnswer.Of(await Asked.Value));

    /// <summary>Drives the browser server named in the environment, under a signature that engine could send.</summary>
    /// <remarks>
    /// The engine is stated here rather than taken from <c>SNAIL_BROWSER_ENGINE</c>, which belongs to the
    /// container fixtures and answers a different question: the image those start and the server this drives
    /// are two things that legitimately differ — stock Chromium in a container beside the patched Gecko build
    /// on a server. Reading one variable for both made that inexpressible, and turned two Chromium-only checks
    /// in another suite red for no reason anyone could see from their failure.
    /// <para>
    /// The signature is set for the same reason it is set in the fixtures: the built-in fingerprints are
    /// Chrome, and a Gecko browser carrying a Chrome user agent was measured to cost the results it was
    /// supposed to fetch.
    /// </para>
    /// </remarks>
    private static async Task<SearchAnswer> AskAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Web:Scraping:BrowserServer"] = LiveFactAttribute.Server,
            ["Web:Scraping:Engine"] = "Firefox",
            ["Web:Identity:Fingerprints:0:UserAgent"] = "Mozilla/5.0 (X11; Linux x86_64; rv:155.0) Gecko/20100101 Firefox/155.0",
            ["Web:Identity:Fingerprints:0:Locale"] = "en-US",
            ["Web:Identity:Fingerprints:0:AcceptLanguage"] = "en-US,en;q=0.9"
        }).Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddGoogle(configuration);

        await using var provider = services.BuildServiceProvider();

        return await provider.GetRequiredService<IEnumerable<ISearchProvider>>()
            .Single(engine => engine.Name == Google.ProviderName)
            .FindAsync(Query);
    }
}
