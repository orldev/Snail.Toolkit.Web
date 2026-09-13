using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.Toolkit.Web.Search.Browser.Extensions;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// Holds the registered engines to the rules the provider contract is documented by.
/// </summary>
/// <remarks>
/// This is the one test project that sees every engine at once — the browser package references the HTTP one —
/// so it is where a rule about all four of them can be asserted.
/// </remarks>
public class GuardTests
{
    /// <summary>Every engine answers with the constant it is registered and configured under.</summary>
    /// <remarks>
    /// The contract asks for the name to be a <c>const</c> on the implementation and for the property to return
    /// that constant, so that registration and runtime cannot drift. Nothing checked it: a name typed twice is
    /// a provider whose cooldown is recorded under one spelling and read under another, and the engine then
    /// never rests however often it refuses. The HTTP client is keyed by the same constant, so a drift there
    /// hands the engine a client with none of its handlers.
    /// </remarks>
    [Fact]
    public async Task EveryRegisteredEngine_AnswersWithTheConstantItIsRegisteredUnder()
    {
        await using var provider = Built();

        foreach (var engine in provider.GetRequiredService<IEnumerable<ISearchProvider>>())
        {
            var declared = engine.GetType()
                .GetField("ProviderName", BindingFlags.Public | BindingFlags.Static)
                ?.GetRawConstantValue();

            Assert.Equal(declared, engine.Name);
        }
    }

    /// <summary>No two engines answer to the same name.</summary>
    /// <remarks>
    /// The name keys the cooldown, the metrics tag and the HTTP client alike, so two engines sharing one would
    /// rest each other and share a client neither was configured with.
    /// </remarks>
    [Fact]
    public async Task RegisteredEngines_AnswerToNamesOfTheirOwn()
    {
        await using var provider = Built();

        var names = provider.GetRequiredService<IEnumerable<ISearchProvider>>()
            .Select(engine => engine.Name)
            .ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The HTTP half of search carries no browser driver.</summary>
    /// <remarks>
    /// The second dependency rule, and the reason this package exists at all: a consumer taking only the HTTP
    /// engines must never pull in Playwright. Asserted on what the assembly references rather than on what has
    /// happened to be loaded, because an assembly is loaded lazily and a test that asks after one query has
    /// proved only that the query did not need it yet.
    /// </remarks>
    [Fact]
    public void Search_BrowserDriver_IsNotReferenced()
        => Assert.DoesNotContain(
            typeof(ISearchProvider).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Microsoft.Playwright", StringComparison.Ordinal) is true);

    private static ServiceProvider Built()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBrowserSearch(new ConfigurationBuilder().Build());

        return services.BuildServiceProvider();
    }
}
