using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.Toolkit.Web.Search.Browser.Extensions;
using Snail.Toolkit.Web.Tests;
using Snail.Toolkit.Web.Tests.Browsers;
using Xunit.Abstractions;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// Asks the live engine one query and checks the answer is made of addresses.
/// </summary>
/// <remarks>
/// This engine is read at its own results address and its markup is anchored on data attributes, because the
/// class names carry a build hash that changes with every deployment. A layout change is therefore the likely
/// way for it to break, and against fixture markup it would break silently: the parser would find nothing and
/// the provider would answer success with no results, which reads exactly like a quiet day.
/// <para>
/// Skipped unless <see cref="LiveFactAttribute.Variable"/> names a browser server, for the same reason the
/// Google check is: reaching a third party is not something a suite should do unasked.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Category", "Live")]
public class LiveBraveTests(ITestOutputHelper output)
{
    private const string Query = "postgresql documentation";
    private const string Expected = "postgresql.org";

    /// <summary>
    /// The engine answers the query it was asked, and the answer names the site the query is about.
    /// </summary>
    /// <remarks>
    /// Measured on 2026-09-12 from a datacentre address, this engine answered a hundred links across
    /// twenty-six sites without wrapping any of them, which is why it is read at an address at all. On
    /// 2026-09-14 the same address was met with <c>429</c> on every attempt, and this check was red for most
    /// of a night while the cause was blamed on the address and then on the signature. Both were wrong: the
    /// engine counts requests, one page load here costs 71 to 73 of them, and the budget was being spent
    /// inside a single page. Asking for the document alone turned three refusals out of three into three
    /// answers out of three from that same address, and this check green.
    /// <para>
    /// So a <c>429</c> here is worth reading twice before it is called a refusal. It says the engine counted
    /// more requests than it was willing to serve, and the first place to look is how many this side made.
    /// </para>
    /// </remarks>
    [LiveFact]
    public async Task FindAsync_AQueryWhoseAnswerIsKnown_NamesTheSiteItIsAbout()
    {
        await using var host = Host();

        var answer = await host.GetRequiredService<IEnumerable<ISearchProvider>>()
            .Single(provider => provider.Name == Brave.ProviderName)
            .FindAsync(Query);

        Assert.True(answer.IsSuccess, answer.Failure?.ToString());

        foreach (var result in answer.Value)
            output.WriteLine($"{result.Address.Canonical}  —  {result.Title}");

        Assert.NotEmpty(answer.Value);
        Assert.All(answer.Value, result => Assert.True(result.Address.IsAbsolute, result.Address.Text));
        Assert.Contains(answer.Value, result => result.Address.Address!.Host.EndsWith(Expected, StringComparison.OrdinalIgnoreCase));
    }

    private ServiceProvider Host()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Web:Scraping:BrowserServer"] = LiveFactAttribute.Server,
            ["Web:Scraping:Engine"] = BrowserImage.Engine.ToString()
        }).Build();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddXUnit(output).SetMinimumLevel(LogLevel.Warning));
        services.AddBrave(configuration);

        return services.MatchingFingerprint().BuildServiceProvider();
    }
}
