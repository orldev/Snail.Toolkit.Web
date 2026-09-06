using System.Net;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Search.Providers;

namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// Covers the one provider's markup, against fixture HTML rather than the live endpoint.
/// </summary>
public class DuckDuckGoTests
{
    private const string Page =
        """
        <html><body>
          <div class="result results_links web-result">
            <a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Fgrain&amp;rut=x">Grain &amp; barley</a>
            <a class="result__snippet">Wheat prices rose &amp; barley held.</a>
          </div>
          <div class="result results_links web-result">
            <a class="result__a" href="https://plain.example/direct">Direct link</a>
          </div>
          <div class="result results_links web-result">
            <span>no link here</span>
          </div>
        </body></html>
        """;

    /// <summary>The outbound link is wrapped in a redirect; the wrapper is not the address anyone wants.</summary>
    [Fact]
    public void Parsed_WrappedLink_UnwrapsTheRealAddress()
    {
        var first = DuckDuckGo.Parsed(Page).First();

        Assert.Equal("https://example.com/grain", first.Address.Canonical);
    }

    [Fact]
    public void Parsed_Entities_AreDecodedInTitleAndSnippet()
    {
        var first = DuckDuckGo.Parsed(Page).First();

        Assert.Equal("Grain & barley", first.Title);
        Assert.Equal("Wheat prices rose & barley held.", first.Snippet);
    }

    [Fact]
    public void Parsed_UnwrappedLink_IsLeftAlone()
        => Assert.Equal("https://plain.example/direct", DuckDuckGo.Parsed(Page).Last().Address.Canonical);

    /// <summary>A block without a link is markup that changed, not a result.</summary>
    [Fact]
    public void Parsed_BlockWithoutALink_IsSkipped()
        => Assert.Equal(2, DuckDuckGo.Parsed(Page).Count());

    [Fact]
    public void Parsed_ResultWithoutASnippet_ReportsAnEmptyOne()
        => Assert.Equal(string.Empty, DuckDuckGo.Parsed(Page).Last().Snippet);

    [Fact]
    public void Parsed_EveryResult_NamesTheProvider()
        => Assert.All(DuckDuckGo.Parsed(Page), result => Assert.Equal(DuckDuckGo.ProviderName, result.Provider));

    /// <summary>
    /// A challenge page, a changed layout and a genuinely empty answer all look the same to the parser, and an
    /// empty list is the honest answer to all three — the caller records it as an answered attempt.
    /// </summary>
    [Fact]
    public void Parsed_MarkupWithoutResults_ReportsNothingRatherThanThrowing()
        => Assert.Empty(DuckDuckGo.Parsed("<html><body><h1>Checking your browser</h1></body></html>"));

    [Fact]
    public async Task FindAsync_BlankQuery_AsksNobody()
    {
        var clients = new StubClients(Page);

        Assert.Empty(await Provider(clients).FindAsync("   "));
        Assert.Null(clients.Requested);
    }

    [Fact]
    public async Task FindAsync_Query_IsEscapedIntoTheEndpoint()
    {
        var clients = new StubClients(Page);

        await Provider(clients).FindAsync("grain & barley");

        Assert.Equal("https://html.duckduckgo.com/html/?q=grain%20%26%20barley", clients.Requested?.AbsoluteUri);
    }

    [Fact]
    public async Task FindAsync_MoreResultsThanAllowed_StopsAtTheCeiling()
    {
        var results = await Provider(new StubClients(Page), maxResults: 1).FindAsync("grain");

        Assert.Single(results);
    }

    /// <summary>
    /// A transport failure is thrown, not swallowed: a provider that answered an empty list on a 503 would be
    /// indistinguishable from one that simply found nothing.
    /// </summary>
    [Fact]
    public async Task FindAsync_ProviderRefuses_Throws()
    {
        var clients = new StubClients(Page, HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(clients).FindAsync("grain"));
    }

    /// <summary>
    /// One page answers most queries, and a caller that asked for more than a page used to be handed a page
    /// with nothing saying so.
    /// </summary>
    [Fact]
    public async Task FindAsync_MoreResultsWantedThanOnePageHolds_AsksForTheNextPage()
    {
        var clients = new PagedClients();

        var results = await Provider(clients, maxResults: 4).FindAsync("grain");

        Assert.Equal(4, results.Count);
        Assert.Equal(
            ["https://example.com/0", "https://example.com/1", "https://example.com/2", "https://example.com/3"],
            results.Select(result => result.Address.Canonical));
    }

    /// <summary>The offset comes from the page's own next-page control, never from a page size guessed here.</summary>
    [Fact]
    public async Task FindAsync_SecondPage_IsAskedForAtTheOffsetThePageItselfNamed()
    {
        var clients = new PagedClients();

        await Provider(clients, maxResults: 4).FindAsync("grain");

        Assert.Equal(
            ["https://html.duckduckgo.com/html/?q=grain", "https://html.duckduckgo.com/html/?q=grain&s=2"],
            clients.Asked.Select(address => address.AbsoluteUri));
    }

    [Fact]
    public async Task FindAsync_OnePageIsEnough_AsksOnce()
    {
        var clients = new PagedClients();

        await Provider(clients, maxResults: 2).FindAsync("grain");

        Assert.Single(clients.Asked);
    }

    /// <summary>A page offering no next control is the last one, and asking again would be a wasted request.</summary>
    [Fact]
    public async Task FindAsync_LastPage_IsNotFollowedByAnotherRequest()
    {
        var clients = new PagedClients(isEndless: false);

        await Provider(clients, maxResults: 30).FindAsync("grain");

        Assert.Equal(2, clients.Asked.Count);
    }

    /// <summary>A page that repeats what the last one said is the end of the results, however it is served.</summary>
    [Fact]
    public async Task FindAsync_PageRepeatingTheOneBefore_StopsAsking()
    {
        var clients = new PagedClients(repeats: true);

        var results = await Provider(clients, maxResults: 30).FindAsync("grain");

        Assert.Equal(2, results.Count);
        Assert.Equal(2, clients.Asked.Count);
    }

    private static DuckDuckGo Provider(IHttpClientFactory clients, int maxResults = 30)
        => new(clients, Options.Create(new SearchOptions { MaxResultsPerProvider = maxResults }));

    private sealed class StubClients(string page, HttpStatusCode status = HttpStatusCode.OK) : IHttpClientFactory
    {
        private readonly StubHandler _handler = new(page, status);

        public Uri? Requested => _handler.Requested;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    /// <summary>Answers two results a page, so that asking for more than two has to ask twice.</summary>
    private sealed class PagedClients(bool repeats = false, bool isEndless = true) : IHttpClientFactory
    {
        private readonly PagedHandler _handler = new(repeats, isEndless);

        public IReadOnlyList<Uri> Asked => _handler.Asked;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class PagedHandler(bool repeats, bool isEndless) : HttpMessageHandler
    {
        private readonly List<Uri> _asked = [];

        public IReadOnlyList<Uri> Asked => _asked;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _asked.Add(request.RequestUri!);

            var page = _asked.Count;
            var offset = repeats ? 0 : (page - 1) * 2;
            var isLast = !isEndless && page > 1;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Results(offset) + (isLast ? string.Empty : Next(offset + 2)))
            });
        }

        private static string Results(int offset)
            => string.Concat(Enumerable.Range(offset, 2).Select(index =>
                $"""
                 <div class="web-result">
                   <a class="result__a" href="https://example.com/{index}">Result {index}</a>
                 </div>
                 """));

        private static string Next(int offset)
            => $"""<form><input type="hidden" name="s" value="{offset}"><input type="submit" value="Next"></form>""";
    }

    private sealed class StubHandler(string page, HttpStatusCode status) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(page) });
        }
    }
}
