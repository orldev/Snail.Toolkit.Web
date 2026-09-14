using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.Toolkit.Web.Search.Browser.Agent.Extensions;
using Snail.Toolkit.Web.Tests;
using Snail.Toolkit.Web.Tests.Browsers;
using Xunit.Abstractions;

namespace Snail.Toolkit.Web.Search.Browser.Agent.Tests;

/// <summary>
/// Asks the live engine one query and checks the answer is made of addresses.
/// </summary>
/// <remarks>
/// The rest of this suite drives a stubbed agent over fixture markup, which is what makes it hermetic and also
/// what leaves the whole point untested: that this engine answers a browser at all, and that the links it
/// answers with survive into addresses. Every defect this package was written for lived in that gap — the
/// results address answering <c>/sorry/</c> where the form answers results, the stealth script undoing the
/// build it runs on, and a page of wrapped links parsing into nothing.
/// <para>
/// Skipped unless <see cref="LiveFactAttribute.Variable"/> names a browser server. Asking a third party over
/// the public internet is not something a suite should do because somebody typed <c>dotnet test</c>, and this
/// engine in particular answers frequency with a challenge.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Category", "Live")]
public class LiveGoogleTests(ITestOutputHelper output)
{
    private const string Query = "postgresql documentation";
    private const string Expected = "postgresql.org";

    /// <summary>
    /// The engine answers the query it was asked, and every link comes back as somewhere to go.
    /// </summary>
    /// <remarks>
    /// The expectation is a fact about the world rather than about whether something came back: the official
    /// documentation is what this query is answered with, and asserting a count instead would pass on a page of
    /// the engine's own furniture. The other three assertions each pin a failure that has actually happened —
    /// a refusal reported as success, wrappers left unresolved, and the engine's own links counted as results.
    /// </remarks>
    [LiveFact]
    public async Task FindAsync_AQueryWhoseAnswerIsKnown_ComesBackAsAddressesOffTheEngine()
    {
        await using var host = Host();

        var answer = await host.GetRequiredService<IEnumerable<ISearchProvider>>()
            .Single(provider => provider.Name == Google.ProviderName)
            .FindAsync(Query);

        Assert.True(answer.IsSuccess, answer.Failure?.ToString());

        foreach (var result in answer.Value)
            output.WriteLine($"{result.Address.Canonical}  —  {result.Title}");

        Assert.NotEmpty(answer.Value);
        Assert.All(answer.Value, result => Assert.True(result.Address.IsAbsolute, result.Address.Text));
        Assert.DoesNotContain(answer.Value, result => IsTheEngineItself(result.Address));
        Assert.Contains(answer.Value, result => result.Address.Address!.Host.EndsWith(Expected, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTheEngineItself(Link address)
        => address.Address!.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase);

    private ServiceProvider Host()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Web:Scraping:BrowserServer"] = LiveFactAttribute.Server,
            ["Web:Scraping:Engine"] = BrowserImage.Engine.ToString()
        }).Build();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddXUnit(output).SetMinimumLevel(LogLevel.Warning));
        services.AddGoogle(configuration);

        return services.MatchingFingerprint().BuildServiceProvider();
    }
}
