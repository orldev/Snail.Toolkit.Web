using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snail.Toolkit.Web.Search.Browser.Extensions;
using Snail.Toolkit.Web.Tests;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// Asks DuckDuckGo's live site one query through a browser and checks the answer is still the shape this package parses.
/// </summary>
/// <remarks>
/// The parser is anchored on the site's <c>data-testid</c> attributes, which nobody here controls; the failure to
/// expect is a page that parses into nothing, which reads exactly like a query that found nothing. The wait is
/// checked by the same answer: the results are drawn by script, so a reader that took the page too early would
/// turn every check below red at once.
/// <para>
/// Firefox, as in the Brave check beside this, and for a measured reason of this site's own: on 2026-11-07 stock
/// headless Chromium was sent to an error page twice out of two, while Firefox from the same address was answered
/// four times out of four.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Category", "Live")]
public class LiveDuckDuckGoTests
{
    private const string Query = "postgresql documentation";
    private const string Expected = "postgresql.org";
    private const string Engine = "duckduckgo.com";

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

    [LiveFact]
    public async Task FindAsync_AtLeastOneResult_CarriesAHeading()
        => LiveAnswer.Titled(LiveAnswer.Of(await Asked.Value));

    [LiveFact]
    public async Task FindAsync_AtLeastOneResult_CarriesADescription()
        => LiveAnswer.Described(LiveAnswer.Of(await Asked.Value));

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
        services.AddDuckDuckGo(configuration);

        await using var provider = services.BuildServiceProvider();

        return await provider.GetRequiredService<IEnumerable<ISearchProvider>>()
            .Single(engine => engine.Name == DuckDuckGo.ProviderName)
            .FindAsync(Query);
    }
}
