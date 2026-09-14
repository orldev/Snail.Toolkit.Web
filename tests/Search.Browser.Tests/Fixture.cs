using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// The pages a stubbed reader hands back, so an engine can be driven without a browser.
/// </summary>
/// <remarks>
/// The text is empty on purpose: a search engine reads its links out of the markup, and a fixture that filled
/// the text in would let a parser pass while reading the wrong half of the page.
/// </remarks>
internal static class Fixture
{
    public static PageOutcome Read(string html)
        => PageOutcome.Success(
            new PageContent(Link.Of("https://engine.example/search"), html, string.Empty, DateTimeOffset.UtcNow));

    public static PageOutcome Refused(FailureKind kind, string message, int? status = null)
        => PageOutcome.Failed(new PageFailure(kind, message) { StatusCode = status });
}

/// <summary>
/// Answers with a prepared page and remembers what it was asked for.
/// </summary>
internal sealed class StubReader(Func<Link, PageOutcome> answer) : IPageReader
{
    public Link? Requested { get; private set; }

    public Appetite? Wanted { get; private set; }

    public Task<PageOutcome> ReadAsync(Link address, CancellationToken ct = default)
        => ReadAsync(address, Appetite.Everything, ct);

    public Task<PageOutcome> ReadAsync(Link address, Appetite appetite, CancellationToken ct = default)
    {
        Requested = address;
        Wanted = appetite;

        return Task.FromResult(answer(address));
    }
}
