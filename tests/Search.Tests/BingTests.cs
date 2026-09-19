using Snail.Toolkit.Web.Detection;
using System.Net;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Search.Providers;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// Covers the one thing Bing contributes over the other HTTP provider: its markup and its wrapper.
/// </summary>
public class BingTests
{
    /// <summary>
    /// Every result address is wrapped in a redirect with the real one base64url-encoded behind «a1».
    /// </summary>
    /// <remarks>
    /// Left wrapped, every result would be bing.com and deduplication across providers would collapse the
    /// whole answer to a single entry — the one failure that would look like an engine with nothing to say.
    /// </remarks>
    [Fact]
    public void Unwrapped_AWrappedAddress_IsTheRealOne()
        => Assert.Equal(
            "https://www.gazprom.ru/",
            Bing.Unwrapped("https://www.bing.com/ck/a?!&&p=8dc9&ptn=3&ver=2&hsh=4&u=a1aHR0cHM6Ly93d3cuZ2F6cHJvbS5ydS8&ntb=1"));

    [Fact]
    public void Unwrapped_AnAddressThatIsNotWrapped_IsLeftAlone()
        => Assert.Equal("https://example.com/x", Bing.Unwrapped("https://example.com/x"));

    /// <summary>A wrapper that stopped being base64 costs its own address, not the whole search.</summary>
    [Fact]
    public void Unwrapped_AWrapperThatStoppedBeingBase64_FallsBackToTheHref()
    {
        const string broken = "https://www.bing.com/ck/a?u=a1_____&ntb=1";

        Assert.Equal(broken, Bing.Unwrapped(broken));
    }

    /// <summary>
    /// The engine's own page goes through the same door every other page does, ceiling included.
    /// </summary>
    /// <remarks>
    /// Until 2026-09-15 the four engines built their own parser, and the ceiling on nesting depth — measured
    /// at 400 ms of CPU for 161 KB nested five thousand deep, 5.6 s at twenty thousand — guarded every page
    /// this library reads EXCEPT the ones it asks for itself.
    /// </remarks>
    /// <summary>A wall under a success status is a refusal here too: the cure is not one engine's.</summary>
    [Fact]
    public async Task FindAsync_AChallengePageUnderASuccessStatus_IsARefusalRatherThanAnEmptyAnswer()
    {
        var answer = await Provider(new StubClients("<html><body>Unusual traffic from your network</body></html>"))
            .FindAsync("grain");

        Assert.False(answer.IsSuccess);
        Assert.Equal(SearchFailureKind.Refused, answer.Failure.Kind);
        Assert.Contains("unusual traffic", answer.Failure.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parsed_MarkupNestedDeeperThanItMayBe_IsRefusedRatherThanRead()
        => Assert.Throws<MarkupTooDeepException>(() => Bing.Parsed(Nested(5_000)).ToList());

    /// <summary>The refusal reaches the caller as data, named, and not as an empty page.</summary>
    /// <remarks>
    /// <see cref="SearchFailureKind.NoResults"/> would be the one untrue answer available here: it is a
    /// statement about the query, and the query is not what went wrong.
    /// </remarks>
    [Fact]
    public async Task FindAsync_MarkupNestedDeeperThanItMayBe_IsUnreadableRatherThanEmpty()
    {
        var answer = await Provider(new StubClients(Nested(5_000))).FindAsync("grain");

        Assert.False(answer.IsSuccess);
        Assert.Equal(SearchFailureKind.Unreadable, answer.Failure.Kind);
        Assert.Contains(Bing.ProviderName, answer.Failure.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parsed_AMarkupWithoutResults_ReportsNothing()
        => Assert.Empty(Bing.Parsed("""<html><body><ol id="b_results"></ol></body></html>"""));

    [Fact]
    public void Parsed_AResultBlock_ReadsTitleAddressAndSnippet()
    {
        const string html = """
            <ol id="b_results"><li class="b_algo"><h2><a href="https://www.bing.com/ck/a?u=a1aHR0cHM6Ly93d3cuZ2F6cHJvbS5ydS8&ntb=1">ПАО «Газпром»</a></h2>
            <div class="b_caption"><p class="b_lineclamp2">Крупнейшая газовая компания.</p></div></li></ol>
            """;

        var found = Bing.Parsed(html).Single();

        Assert.Equal("ПАО «Газпром»", found.Title);
        Assert.Equal("https://www.gazprom.ru/", found.Address.Canonical);
        Assert.Equal("Крупнейшая газовая компания.", found.Snippet);
        Assert.Equal(Bing.ProviderName, found.Provider);
    }

    /// <summary>
    /// The page this engine is known to serve instead of an answer: ten confident results about something
    /// else entirely. It is a refusal, not an empty answer and not a list of links.
    /// </summary>
    /// <remarks>
    /// MEASURED on 2026-09-15 from a datacentre address: «"Роснано"» came back as biblical sites and Netflix,
    /// «отзыв лицензии КИВИ Банк» as car parts and Microsoft sign-in pages. The browser reading of this engine
    /// has distrusted such a page since 2026-09-15; the HTTP reading kept handing it on, which is the fifteenth
    /// item on the consumer's list. The rule is one for both roads since 2026-09-20.
    /// </remarks>
    [Fact]
    public async Task FindAsync_PageOfResultsAboutSomethingElse_IsARefusalRatherThanAnAnswer()
    {
        var answer = await Provider(new StubClients(Unrelated(10))).FindAsync("отзыв лицензии КИВИ Банк");

        Assert.False(answer.IsSuccess);
        Assert.Equal(SearchFailureKind.Refused, answer.Failure.Kind);
        Assert.Contains(Bing.ProviderName, answer.Failure.Message!, StringComparison.Ordinal);
    }

    /// <summary>And the page that does answer is handed on, which is what says the rule is not a blanket.</summary>
    [Fact]
    public async Task FindAsync_PageOfResultsAboutTheQuery_IsAnAnswer()
    {
        var answer = await Provider(new StubClients(About(10, "лицензии"))).FindAsync("отзыв лицензии КИВИ Банк");

        Assert.True(answer.IsSuccess);
        Assert.Equal(10, answer.Value.Count);
    }

    private static string Unrelated(int results) => About(results, "Rooms and rates");

    private static string About(int results, string subject)
        => "<ol id=\"b_results\">"
           + string.Concat(Enumerable.Range(0, results).Select(index =>
               $"""<li class="b_algo"><h2><a href="https://example.com/{index}">{subject} {index}</a></h2></li>"""))
           + "</ol>";

    /// <summary>The engine with a real block detector, because a wall is read before the markup is parsed.</summary>
    private static Bing Provider(IHttpClientFactory clients)
        => new(clients, new BlockDetector(), Options.Create(new SearchOptions()));

    private static string Nested(int depth)
        => string.Concat(Enumerable.Repeat("<div>", depth))
           + "hello"
           + string.Concat(Enumerable.Repeat("</div>", depth));

    /// <summary>Answers every request with one page, so the reading is what is under test.</summary>
    private sealed class StubClients(string page, HttpStatusCode status = HttpStatusCode.OK) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(page, status));

        private sealed class StubHandler(string page, HttpStatusCode status) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(page) });
        }
    }
}
