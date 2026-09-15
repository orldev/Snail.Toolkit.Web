using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Text;
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

        Assert.Empty((await Provider(clients).FindAsync("   ")).Value);
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
        var answer = await Provider(new StubClients(Page), maxResults: 1).FindAsync("grain");

        Assert.Single(answer.Value);
    }

    /// <summary>
    /// A status that is not success is reported, never swallowed: a provider that answered an empty list on a
    /// 503 would be indistinguishable from one that simply found nothing.
    /// </summary>
    /// <remarks>
    /// The three statuses here are the engine asking to be left alone rather than failing, so they come back as
    /// a refusal and the search rests the engine. Until 2026-09-13 this provider called
    /// <c>EnsureSuccessStatusCode</c>, so every one of them arrived as a fault — which leaves the record
    /// untouched — and the engine that had just asked for a pause was asked again at once.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task FindAsync_EngineAskingToBeLeftAlone_IsARefusal(HttpStatusCode status)
    {
        var answer = await Provider(new StubClients(Page, status)).FindAsync("grain");

        Assert.Equal(SearchFailureKind.Refused, answer.Failure?.Kind);
    }

    /// <summary>A status that says nothing about the engine's patience is a fault, and rests nobody.</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FindAsync_EngineThatSimplyFailed_IsAFault(HttpStatusCode status)
    {
        var answer = await Provider(new StubClients(Page, status)).FindAsync("grain");

        Assert.Equal(SearchFailureKind.Faulted, answer.Failure?.Kind);
    }

    /// <summary>
    /// One page answers most queries, and a caller that asked for more than a page used to be handed a page
    /// with nothing saying so.
    /// </summary>
    [Fact]
    public async Task FindAsync_MoreResultsWantedThanOnePageHolds_AsksForTheNextPage()
    {
        var clients = new PagedClients();

        var answer = await Provider(clients, maxResults: 4).FindAsync("grain");

        Assert.Equal(4, answer.Value.Count);
        Assert.Equal(
            ["https://example.com/0", "https://example.com/1", "https://example.com/2", "https://example.com/3"],
            answer.Value.Select(result => result.Address.Canonical));
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

        var answer = await Provider(clients, maxResults: 30).FindAsync("grain");

        Assert.Equal(2, answer.Value.Count);
        Assert.Equal(2, clients.Asked.Count);
    }

    /// <summary>
    /// A challenge page is a refusal even when the status says success — and saying otherwise is the one
    /// answer here that would be untrue.
    /// </summary>
    /// <remarks>
    /// MEASURED 15.09.2026 from a datacentre address: this engine answered <c>202</c> with a captcha page.
    /// Status <c>202</c> is a success, so nothing was read as a refusal; the parser found no result blocks in
    /// the wall and the search recorded NO RESULTS — a statement about the QUERY — for a page that was a
    /// statement about the ADDRESS. A dossier built on that says "nothing was written about this company on
    /// the open web" when the truth is "we were not let in".
    /// <para>
    /// The second cost was invisible and worse: only a refusal rests an engine, so the wall was asked again
    /// at the next query, which is how a wall becomes a ban.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task FindAsync_AChallengePageUnderASuccessStatus_IsARefusalRatherThanAnEmptyAnswer()
    {
        const string wall =
            """
            <!doctype html><html><body>
            <div class="anomaly-modal__description">Please complete the following challenge to confirm this search was made by a human.</div>
            </body></html>
            """;

        var answer = await Provider(new StubClients(wall, HttpStatusCode.Accepted)).FindAsync("grain");

        Assert.False(answer.IsSuccess);
        Assert.Equal(SearchFailureKind.Refused, answer.Failure.Kind);
        Assert.Contains(DuckDuckGo.ProviderName, answer.Failure.Message!, StringComparison.Ordinal);
        Assert.Contains("confirm this search was made by a human", answer.Failure.Message!, StringComparison.Ordinal);
    }

    /// <summary>The engine's own page goes through the same door every other page does, ceiling included.</summary>
    [Fact]
    public void Parsed_MarkupNestedDeeperThanItMayBe_IsRefusedRatherThanRead()
        => Assert.Throws<MarkupTooDeepException>(() => DuckDuckGo.Parsed(Nested(5_000)).ToList());

    /// <summary>
    /// The next-page control is read out of the same markup, so it is the second way in and is covered too.
    /// </summary>
    [Fact]
    public void NextOffset_MarkupNestedDeeperThanItMayBe_IsRefusedRatherThanRead()
        => Assert.Throws<MarkupTooDeepException>(() => DuckDuckGo.NextOffset(Nested(5_000)));

    /// <summary>The refusal reaches the caller as data, named, and not as an empty page.</summary>
    [Fact]
    public async Task FindAsync_MarkupNestedDeeperThanItMayBe_IsUnreadableRatherThanEmpty()
    {
        var answer = await Provider(new StubClients(Nested(5_000))).FindAsync("grain");

        Assert.False(answer.IsSuccess);
        Assert.Equal(SearchFailureKind.Unreadable, answer.Failure.Kind);
        Assert.Contains(DuckDuckGo.ProviderName, answer.Failure.Message!, StringComparison.Ordinal);
    }

    private static string Nested(int depth)
        => string.Concat(Enumerable.Repeat("<div>", depth))
           + "hello"
           + string.Concat(Enumerable.Repeat("</div>", depth));

    private static DuckDuckGo Provider(IHttpClientFactory clients, int maxResults = 30)
        => new(
            clients,
            new BlockDetector(),
            Options.Create(new SearchOptions { MaxResultsPerProvider = maxResults }));

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
