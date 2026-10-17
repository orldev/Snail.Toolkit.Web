using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.Toolkit.Web.Search;
using Snail.Toolkit.Web.Search.Browser.Agent;
using Snail.Toolkit.Web.Search.Browser.Agent.Extensions;

namespace Snail.Toolkit.Web.Probes;

/// <summary>
/// Runs one search the way a consumer will: the registered provider, the real agent, the real client.
/// </summary>
/// <remarks>
/// Nothing here is stubbed, which is the point. A green unit suite says the parser reads the markup it was
/// given; it says nothing about whether the engine answers a browser at all, or whether the links survive into
/// addresses.
/// <para>
/// The three numbers at the end are what makes this hard to fool. A count of results passes on a page of the
/// engine's own furniture and on a page of unresolved wrappers alike; "none left wrapped" and "none left on
/// the engine's host" are the two ways that count lies.
/// </para>
/// </remarks>
internal static class Answering
{
    private const string Query = "postgresql documentation";

    public static async Task ReportAsync(string endpoint)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Web:Scraping:BrowserServer"] = endpoint,
            ["Web:Scraping:Engine"] = "Firefox",
            ["Web:Identity:Fingerprints:0:UserAgent"] = "Mozilla/5.0 (X11; Linux x86_64; rv:155.0) Gecko/20100101 Firefox/155.0",
            ["Web:Identity:Fingerprints:0:Locale"] = "en-US",
            ["Web:Identity:Fingerprints:0:Timezone"] = "America/New_York",
            ["Web:Identity:Fingerprints:0:AcceptLanguage"] = "en-US,en;q=0.9"
        }).Build();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddGoogle(configuration);

        await using var container = services.BuildServiceProvider();

        var engine = container.GetRequiredService<IEnumerable<ISearchProvider>>()
            .Single(provider => provider.Name == Google.ProviderName);

        Console.WriteLine($"{DateTime.Now:HH:mm} — the whole search through the registered provider");

        var started = DateTimeOffset.UtcNow;
        var answer = await engine.FindAsync(Query);
        var spent = DateTimeOffset.UtcNow - started;

        if (!answer.IsSuccess)
        {
            Console.WriteLine($"  REFUSED                  {answer.Failure}");

            return;
        }

        Console.WriteLine($"  results                  {answer.Value.Count}, in {spent.TotalSeconds:F0} s");

        foreach (var result in answer.Value)
            Console.WriteLine($"    {(result.Address.IsAbsolute ? "address" : "WRAPPER")}  {result.Address.Canonical[..Math.Min(62, result.Address.Canonical.Length)]}");

        var wrapped = answer.Value.Count(result => !result.Address.IsAbsolute);
        var own = answer.Value.Count(result => result.Address.Address?.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase) == true);

        Console.WriteLine($"  unresolved               {wrapped}");
        Console.WriteLine($"  left on the engine       {own}");
        Console.WriteLine($"  first title              {answer.Value.FirstOrDefault()?.Title}");
    }
}
