using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snail.Toolkit.Web.Search.Browser.Extensions;
using Snail.Toolkit.Web.Tests;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// Asks the live engine one query and checks the answer is still made of the shape this package parses.
/// </summary>
/// <remarks>
/// This engine is read at its own results address and its blocks are found by <c>data-type="web"</c>, but its
/// heading and prose are read from class names that carry a build hash — <c>search-snippet-title</c>,
/// <c>generic-snippet</c> — and those change with every deployment. Against recorded markup that breaks
/// silently: the parser finds the blocks, reads nothing out of them, and the provider answers success with
/// results that have no words in them.
/// <para>
/// Measured on 2026-09-12 from a datacentre address, this engine answered a hundred links across twenty-six
/// sites without wrapping any of them. On 2026-09-14 the same address was met with <c>429</c> on every
/// attempt, and the cause was blamed on the address and then on the signature before either was measured.
/// Both were wrong: the engine counts requests, one page load here costs 71 to 73 of them, and the budget was
/// being spent inside a single page. Asking for the document alone turned three refusals out of three into
/// three answers out of three from that same address. So a <c>429</c> here is worth reading twice before it
/// is called a refusal — the first place to look is how many requests this side made.
/// </para>
/// <para>
/// That the results are in the document at all needs no check of its own: this provider asks for the document
/// alone, so every check below already proves it. Were the engine to render its results in the browser
/// instead, they would all go red together — which is the right amount of noise for one fact.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Category", "Live")]
public class LiveBraveTests
{
    private const string Query = "postgresql documentation";
    private const string Expected = "postgresql.org";
    private const string Engine = "search.brave.com";

    private static readonly Lazy<Task<SearchAnswer>> Asked = new(AskAsync);

    [LiveFact]
    public async Task FindAsync_ARealQuery_IsAnsweredWithResults()
        => LiveAnswer.Of(await Asked.Value);

    [LiveFact]
    public async Task FindAsync_EveryResult_LeadsAwayFromTheEngine()
        => LiveAnswer.LeadsAway(LiveAnswer.Of(await Asked.Value), Engine);

    [LiveFact]
    public async Task FindAsync_TheAnswer_NamesTheSiteTheQueryIsAbout()
        => LiveAnswer.About(LiveAnswer.Of(await Asked.Value), Expected);

    /// <summary>The heading is read from a class name that carries a build hash, so it is worth watching.</summary>
    [LiveFact]
    public async Task FindAsync_AtLeastOneResult_CarriesAHeading()
        => LiveAnswer.Titled(LiveAnswer.Of(await Asked.Value));

    /// <summary>And the prose from two more of them.</summary>
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
        services.AddBrave(configuration);

        await using var provider = services.BuildServiceProvider();

        return await provider.GetRequiredService<IEnumerable<ISearchProvider>>()
            .Single(engine => engine.Name == Brave.ProviderName)
            .FindAsync(Query);
    }
}
