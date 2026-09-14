using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Scraping.Extensions;
using Snail.Toolkit.Web.Search.Extensions;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// The examples the README shows a newcomer, compiled rather than believed.
/// </summary>
/// <remarks>
/// Nothing here runs. It exists to be built: the README ships inside all six packages and is the first and
/// often only thing a consumer reads on NuGet, so an example that stopped compiling is a defect visible to
/// everyone but us. This is the drift that goes unnoticed, since renaming a member fixes every caller in the
/// solution and leaves the prose describing an API that no longer exists.
/// <para>
/// It guards one direction only — the code moving out from under the README. Prose edited to say something
/// the code never did is not caught here, and nothing but reading catches that.
/// </para>
/// <para>
/// It lives in this suite for a dull reason: the examples span searching and reading, and this is the only
/// test project whose references reach both.
/// </para>
/// </remarks>
internal static class ReadmeExamples
{
    /// <summary>Registering the two engines from configuration.</summary>
    public static void Registering(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSearch(configuration);
        services.AddScraping(configuration);
    }

    /// <summary>Asking a question.</summary>
    public static async Task SearchingAsync(IWebSearch search)
    {
        var found = await search.FindAsync(["postgresql documentation"]);

        foreach (var result in found.Results)
            Console.WriteLine($"{result.Title} — {result.Address}");
    }

    /// <summary>Reading a page.</summary>
    public static async Task ReadingAsync(IPageReader pages)
    {
        var outcome = await pages.ReadAsync(Link.Of("https://www.postgresql.org/docs/"));

        if (outcome.IsSuccess)
            Console.WriteLine(outcome.Value.Text);
    }

    /// <summary>Telling an empty answer from an unanswered one.</summary>
    public static void Reading(PageOutcome outcome)
    {
        if (outcome.TryGetValue(out var page))
            Console.WriteLine(page.Title);
        else if (outcome.Failure.Kind is FailureKind.Blocked)
            Console.WriteLine($"Refused: {outcome.Failure.Message}");
    }

    /// <summary>The same question asked of a whole search.</summary>
    public static void Answering(SearchOutcome found)
    {
        Console.WriteLine(found.HasResults);
        Console.WriteLine(found.IsAnswered);
    }
}
