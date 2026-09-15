using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// Covers what Bing contributes over <see cref="BrowserSearch"/>: its address, its markup, the redirect it
/// wraps every result in, and the page it sometimes serves instead of an answer.
/// </summary>
/// <remarks>
/// The markup copies a page recorded from the live engine on 2026-09-15 — the inline style block inside the
/// first result, the <c>b_vtl_deeplinks</c> beside <c>b_algo</c>, the <c>iid</c> attributes and the
/// <c>/ck/a</c> wrapper with its base64url payload are all as the engine serves them.
/// </remarks>
public class BingTests
{
    private const string Page =
        """
        <html><body><ol id="b_results">
          <li class="b_algo b_vtl_deeplinks" data-id="" iid="SERP.5679"><style type="text/css">.b_tpcn cite span{color:red}</style>
            <div class="b_tpcn"><a class="tilk" href="https://www.bing.com/ck/a?!&amp;&amp;p=deadbeef&amp;ptn=3&amp;ver=2&amp;hsh=4&amp;fclid=3213ad9c-833e&amp;u=a1aHR0cHM6Ly9yb3NuYW5vLmV4YW1wbGUvcHJlc3M&amp;ntb=1"><div class="tptt">Группа РОСНАНО</div></a></div>
            <h2><a href="https://www.bing.com/ck/a?!&amp;&amp;p=deadbeef&amp;ptn=3&amp;ver=2&amp;hsh=4&amp;fclid=3213ad9c-833e&amp;u=a1aHR0cHM6Ly9yb3NuYW5vLmV4YW1wbGUvcHJlc3M&amp;ntb=1" target="_blank">Пресс-центр — Группа РОСНАНО</a></h2>
            <div class="b_caption"><p class="b_lineclamp2">Новости Группы РОСНАНО и её портфельных компаний.</p></div>
          </li>
          <li class="b_algo" data-id="" iid="SERP.5683">
            <h2><a href="https://ru.wikipedia.example/Роснано">Роснано — Википедия</a></h2>
            <div class="b_caption"><p>Российская компания, основанная в 2007 году.</p></div>
          </li>
          <li class="b_algo">
            <h2><a href="https://www.bing.com/ck/a?!&amp;&amp;p=deadbeef&amp;ptn=3&amp;ver=2&amp;hsh=4&amp;fclid=3213ad9c-833e&amp;u=a1aHR0cHM6Ly93d3cuYmluZy5jb20vc2VhcmNoP3E9bmV4dA&amp;ntb=1">Ещё по запросу</a></h2>
          </li>
          <li class="b_algo">
            <div class="b_caption"><p>Блок без заголовка вовсе.</p></div>
          </li>
          <li class="b_ans"><h2><a href="https://answer.example/card">Карточка ответа</a></h2></li>
        </ol></body></html>
        """;

    /// <summary>A page whose ten results are about something else entirely, as the engine sometimes serves.</summary>
    private const string Stub =
        """
        <html><body><ol id="b_results">
          <li class="b_algo"><h2><a href="https://netflix.example/watch">Netflix: Ve series online</a></h2>
            <div class="b_caption"><p>Ve peliculas online.</p></div></li>
          <li class="b_algo"><h2><a href="https://bible.example/read">Bible Gateway passage</a></h2>
            <div class="b_caption"><p>Read the passage online.</p></div></li>
        </ol></body></html>
        """;

    [Fact]
    public async Task FindAsync_OrdinaryResult_ReadsTitleAddressAndSnippet()
    {
        var first = (await Found(Page))[0];

        Assert.Equal("Пресс-центр — Группа РОСНАНО", first.Title);
        Assert.Equal("https://rosnano.example/press", first.Address.Canonical);
        Assert.Equal("Новости Группы РОСНАНО и её портфельных компаний.", first.Snippet);
    }

    /// <summary>
    /// The engine wraps every link in a redirect of its own, and the address it leads to is inside it.
    /// </summary>
    /// <remarks>
    /// Unwrapping costs nothing here — the address is carried in the wrapper, base64url after a two-character
    /// marker — which is the whole difference from Google, whose wrapper keeps the address and has to be asked.
    /// A caller handed the wrapper instead would store bing.com as the source of every finding it ever made.
    /// </remarks>
    [Fact]
    public async Task FindAsync_AWrappedLink_IsUnwrappedIntoTheAddressItLeadsTo()
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Canonical.Contains("/ck/a", StringComparison.Ordinal));

    [Fact]
    public async Task FindAsync_ALinkTheEngineStatesPlainly_IsLeftAsItIs()
        => Assert.Contains(await Found(Page), result => result.Address.Canonical.StartsWith("https://ru.wikipedia.example/", StringComparison.Ordinal));

    /// <summary>A wrapper that leads back to the engine is furniture, not an answer.</summary>
    [Fact]
    public async Task FindAsync_AWrappedLinkBackToTheEngine_IsNotAResult()
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Address?.Host == "www.bing.com");

    [Fact]
    public async Task FindAsync_BlockWithoutAHeading_IsSkipped()
        => Assert.Equal(2, (await Found(Page)).Count);

    /// <summary>Only the blocks the page itself calls web results are read.</summary>
    [Fact]
    public async Task FindAsync_BlockOfAnotherKind_IsNotAWebResult()
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Canonical.Contains("answer.example", StringComparison.Ordinal));

    [Fact]
    public async Task FindAsync_EveryResult_NamesTheProvider()
        => Assert.All(await Found(Page), result => Assert.Equal(Bing.ProviderName, result.Provider));

    [Fact]
    public async Task FindAsync_MarkupItCannotFollow_ReportsNothing()
        => Assert.Empty(await Found("<html><body><div>we redesigned everything</div></body></html>"));

    [Fact]
    public async Task FindAsync_Query_IsEscapedIntoTheConfiguredDomain()
    {
        var reader = new StubReader(_ => Fixture.Read(Page));

        await Provider(reader, "www.bing.co.uk").FindAsync("Роснано & сделка");

        Assert.Equal(
            "https://www.bing.co.uk/search?q=%D0%A0%D0%BE%D1%81%D0%BD%D0%B0%D0%BD%D0%BE%20%26%20%D1%81%D0%B4%D0%B5%D0%BB%D0%BA%D0%B0",
            reader.Requested?.Address?.AbsoluteUri);
    }

    /// <summary>The whole page, because the half that would be saved is the half with the results in it.</summary>
    [Fact]
    public async Task FindAsync_Query_AsksForTheWholePage()
    {
        var reader = new StubReader(_ => Fixture.Read(Page));

        await Provider(reader).FindAsync("Роснано");

        Assert.Equal(Appetite.Everything, reader.Wanted);
    }

    /// <summary>
    /// A page of confident results about something else is a refusal, not an answer.
    /// </summary>
    /// <remarks>
    /// Measured live on 2026-09-15: asked in Russian from a datacentre address, this engine answered
    /// «"Роснано"» with bible sites and «отзыв лицензии КИВИ Банк» with car parts — ten results each, in the
    /// right markup. Handed on as results, those become pages the bot reads, judges and reports on, and the
    /// silence that follows reads as a fact about the company rather than about the engine.
    /// </remarks>
    [Fact]
    public async Task FindAsync_APageAboutSomethingElse_IsRefusedRatherThanAnswered()
    {
        var answered = await Provider(new StubReader(_ => Fixture.Read(Stub))).FindAsync("Роснано");

        Assert.False(answered.IsSuccess);
        Assert.Equal(SearchFailureKind.Refused, answered.Failure!.Kind);
    }

    /// <summary>One result carrying the query is enough: the rule catches a wrong page, not a poor answer.</summary>
    [Fact]
    public async Task FindAsync_APageWhereOneResultCarriesTheQuery_IsAnAnswer()
        => Assert.True((await Provider(new StubReader(_ => Fixture.Read(Page))).FindAsync("Роснано")).IsSuccess);

    /// <summary>A query of short words is not judged at all, because short words match by accident.</summary>
    [Fact]
    public async Task FindAsync_AQueryOfShortWordsOnly_IsNotJudged()
        => Assert.True((await Provider(new StubReader(_ => Fixture.Read(Stub))).FindAsync("ООО АБВ")).IsSuccess);

    private static async Task<IReadOnlyList<SearchResult>> Found(string html)
        => (await Provider(new StubReader(_ => Fixture.Read(html))).FindAsync("Роснано")).Value;

    private static Bing Provider(IPageReader reader, string domain = "www.bing.com")
        => new(reader,
            Options.Create(new BingOptions { Domain = domain }),
            Options.Create(new SearchOptions()),
            NullLogger<Bing>.Instance);
}
