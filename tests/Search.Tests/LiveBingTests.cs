using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snail.Toolkit.Web.Search.Extensions;
using Snail.Toolkit.Web.Search.Providers;
using Snail.Toolkit.Web.Tests;

namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// Asks the live engine one query and checks the answer is still made of the shape this package parses.
/// </summary>
/// <remarks>
/// The rest of this suite reads recorded markup, which is what makes it hermetic and also what leaves the one
/// claim untested: that the markup is still what the engine serves. This parser is anchored on class names
/// nobody here controls, so the failure to expect is not an exception but a page that parses into nothing —
/// success with no results, which reads exactly like a query that found nothing.
/// <para>
/// The engine is asked ONCE and the answer read by every check below. A live request is the scarce thing, not
/// a test method: an engine that answers frequency with a challenge cannot be asked five times to make five
/// assertions. Skipped unless <see cref="LiveFactAttribute.Variable"/> says this suite may reach the internet.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Category", "Live")]
public class LiveBingTests
{
    private const string Query = "postgresql documentation";
    private const string Expected = "postgresql.org";
    private const string Engine = "bing.com";

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
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSearch(new ConfigurationBuilder().Build());

        await using var provider = services.BuildServiceProvider();

        return await provider.GetRequiredService<IEnumerable<ISearchProvider>>()
            .Single(engine => engine.Name == Bing.ProviderName)
            .FindAsync(Query);
    }
}
