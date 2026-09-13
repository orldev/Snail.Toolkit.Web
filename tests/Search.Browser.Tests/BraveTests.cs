using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// Covers the one thing Brave contributes over <see cref="BrowserSearch"/>: its address and its markup.
/// </summary>
/// <remarks>
/// The markup copies a page recorded from the live engine on 2026-09-12, down to the build hashes in its class
/// names — <c>svelte-jmfu5f</c>, <c>svelte-14r20fy</c> — because those are exactly what a parser must not
/// depend on. A selector written against them would keep passing here and stop matching the day the engine
/// ships a new build, which from the outside looks like a day when nothing was found.
/// </remarks>
public class BraveTests
{
    private const string Page =
        """
        <html><body><main>
          <div class="snippet svelte-jmfu5f" data-pos="0" data-type="web" data-keynav="true">
            <div class="result-content svelte-1rq4ngz">
              <a href="https://example.com/grain" target="_self" class="svelte-14r20fy l1">
                <div class="site-name-content svelte-on1hvy"><cite class="snippet-url">example.com</cite></div>
                <div class="title search-snippet-title line-clamp-1 svelte-14r20fy" title="Grain &amp; barley report">Grain &amp; barley rep</div>
              </a>
              <div class="generic-snippet svelte-1cwdgg3">
                <div class="content desktop-default-regular t-primary">Wheat prices rose &amp; barley held steady.</div>
              </div>
            </div>
          </div>
          <div class="snippet svelte-jmfu5f" data-pos="1" data-type="web">
            <div class="result-content svelte-1rq4ngz">
              <a href="https://example.org/no-snippet" class="svelte-14r20fy l1">
                <div class="title search-snippet-title svelte-14r20fy" title="Nothing to describe">Nothing to describe</div>
              </a>
            </div>
          </div>
          <div class="snippet svelte-jmfu5f" data-pos="2" data-type="web">
            <div class="result-content svelte-1rq4ngz">
              <a href="https://search.brave.com/settings" class="svelte-14r20fy l1">
                <div class="title search-snippet-title svelte-14r20fy" title="Settings">Settings</div>
              </a>
            </div>
          </div>
          <div class="snippet svelte-jmfu5f" data-pos="3" data-type="web">
            <div class="result-content svelte-1rq4ngz"><a href="https://example.net/untitled" class="l1"></a></div>
          </div>
          <div class="snippet svelte-jmfu5f" data-pos="4" data-type="news">
            <div class="result-content svelte-1rq4ngz">
              <a href="https://news.example/story" class="l1">
                <div class="title search-snippet-title" title="Yesterday in grain">Yesterday in grain</div>
              </a>
            </div>
          </div>
        </main></body></html>
        """;

    [Fact]
    public async Task FindAsync_OrdinaryResult_ReadsTitleAddressAndSnippet()
    {
        var first = (await Found(Page))[0];

        Assert.Equal("Grain & barley report", first.Title);
        Assert.Equal("https://example.com/grain", first.Address.Canonical);
        Assert.Equal("Wheat prices rose & barley held steady.", first.Snippet);
    }

    /// <summary>
    /// The heading's own text is clamped to one line in the markup; the attribute beside it carries it whole.
    /// </summary>
    /// <remarks>
    /// Measured on the recorded page: the element reads "Grain &amp; barley rep" while its <c>title</c>
    /// attribute holds the full sentence. Taking the text would have handed every caller a truncated title and
    /// nothing to say that it was truncated.
    /// </remarks>
    [Fact]
    public async Task FindAsync_ClampedHeading_TakesTheWholeTitleFromTheAttribute()
        => Assert.DoesNotContain(await Found(Page), result => result.Title == "Grain & barley rep");

    [Fact]
    public async Task FindAsync_ResultWithoutASnippet_ReportsAnEmptyOne()
        => Assert.Equal(string.Empty, (await Found(Page))[1].Snippet);

    /// <summary>The engine's own settings page is furniture, not an answer.</summary>
    [Fact]
    public async Task FindAsync_TheEnginesOwnPages_AreNotResults()
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Address?.Host == "search.brave.com");

    [Fact]
    public async Task FindAsync_BlockWithoutATitle_IsSkipped()
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Canonical.Contains("untitled", StringComparison.Ordinal));

    /// <summary>
    /// Only the blocks the page itself calls web results are read.
    /// </summary>
    /// <remarks>
    /// The same page carries news, video and discussion blocks in identical markup, told apart by
    /// <c>data-type</c> alone. Reading them all would mix answers of different kinds under one provider and
    /// hand a caller a news teaser where it asked for a page.
    /// </remarks>
    [Fact]
    public async Task FindAsync_BlockOfAnotherKind_IsNotAWebResult()
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Canonical.Contains("news.example", StringComparison.Ordinal));

    [Fact]
    public async Task FindAsync_EveryResult_NamesTheProvider()
        => Assert.All(await Found(Page), result => Assert.Equal(Brave.ProviderName, result.Provider));

    [Fact]
    public async Task FindAsync_PageThatLoaded_ReturnsOnlyTheOutboundResults()
        => Assert.Equal(2, (await Found(Page)).Count);

    [Fact]
    public async Task FindAsync_MarkupItCannotFollow_ReportsNothing()
        => Assert.Empty(await Found("<html><body><div>we redesigned everything</div></body></html>"));

    [Fact]
    public async Task FindAsync_Query_IsEscapedIntoTheConfiguredDomain()
    {
        var reader = new StubReader(_ => Fixture.Read(Page));

        await Provider(reader, "search.brave.software").FindAsync("grain & barley");

        Assert.Equal(
            "https://search.brave.software/search?q=grain%20%26%20barley",
            reader.Requested?.Address?.AbsoluteUri);
    }

    private static async Task<IReadOnlyList<SearchResult>> Found(string html)
        => (await Provider(new StubReader(_ => Fixture.Read(html))).FindAsync("grain")).Value;

    private static Brave Provider(IPageReader reader, string domain = "search.brave.com")
        => new(reader,
            Options.Create(new BraveOptions { Domain = domain }),
            Options.Create(new SearchOptions()),
            NullLogger<Brave>.Instance);
}
