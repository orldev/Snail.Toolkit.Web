using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Scraping.Agent;

namespace Snail.Toolkit.Web.Search.Browser.Agent.Tests;

/// <summary>
/// The pages a stubbed agent hands back, so an engine can be driven without a browser.
/// </summary>
/// <remarks>
/// The text is empty on purpose: a search engine reads its links out of the markup, and a fixture that filled
/// the text in would let a parser pass while reading the wrong half of the page.
/// </remarks>
internal static class Fixture
{
    public static PageOutcome Walked(string html)
        => PageOutcome.Success(
            new PageContent(Link.Of("https://engine.example/"), html, string.Empty, DateTimeOffset.UtcNow));

    public static PageOutcome Refused(FailureKind kind, string message, int? status = null)
        => PageOutcome.Failed(new PageFailure(kind, message) { StatusCode = status });
}

/// <summary>
/// Answers with a prepared page and remembers the errand it was sent on.
/// </summary>
internal sealed class StubAgent(Func<Errand, PageOutcome> answer) : IPageAgent
{
    public Errand? Performed { get; private set; }

    public Task<PageOutcome> PerformAsync(Errand errand, CancellationToken ct = default)
    {
        Performed = errand;

        return Task.FromResult(answer(errand));
    }
}
