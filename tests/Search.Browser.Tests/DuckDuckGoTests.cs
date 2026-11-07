using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// Covers what DuckDuckGo's site contributes over <see cref="BrowserSearch"/>: its address, its wait and its markup.
/// </summary>
/// <remarks>
/// The markup copies the result blocks of a page recorded from the live site in Firefox on 2026-11-07, build hashes
/// in the class names included — <c>yQDlj3B5DI5YO8c8Ulio</c>, <c>eVNpHGjtxRBq_gLOfGDr</c> — because those are what a
/// parser must not depend on. The menu, the favicon link and the sitelinks are kept too: each is an anchor inside
/// the result block, and a parser taking the first anchor it meets would read one of them instead of the heading.
/// </remarks>
public class DuckDuckGoTests
{
    private const string Page =
        """
        <html><body><section data-testid="mainline"><ol class="react-results--main">
          <li data-layout="organic">
            <article id="r1-0" data-testid="result" data-nrn="result" class="yQDlj3B5DI5YO8c8Ulio CpkrTDP54mqzpuCSn1Fa">
              <div class="OHr0VX9IuNcv6iakvT6A"><div role="menu" class="BdZVYXbdg6Rx9Lrm5wzC" aria-hidden="true">
                <a class="bcz7ZQmpP9fW9gyprTn7" href="?q=grain%20site%3Aexample.com" role="menuitem"><span>Only include results for this site</span></a>
              </div></div>
              <div class="OQ_6vPwNhCeusNiEDcGp">
                <a href="/?q=grain+site:example.com" title="Search domain example.com" data-testid="result-extras-site-search-link"><img src="//external-content.duckduckgo.com/ip3/example.com.ico"></a>
                <a href="https://example.com/grain" data-testid="result-extras-url-link"><span>https://example.com</span><span>&nbsp;›&nbsp;grain</span></a>
              </div>
              <div class="ikg2IXiCD14iVX7AdZo1"><h2 class="LnpumSThxEWMIsDdAT17">
                <a href="https://example.com/grain" rel="noopener" class="eVNpHGjtxRBq_gLOfGDr" data-testid="result-title-a"><span class="EKtkFWMYpwzMKOYr0GYm">Grain &amp; barley report</span></a>
              </h2></div>
              <div class="E2eLOJr8HctVnDOTM8fs"><div class="OgdwYG6KE2qthn9XQWFC" data-result="snippet">
                <div><span class="kY2IgmnCmOGjharHErah"><span>Wheat prices rose &amp; <b>barley</b> held steady.</span></span></div>
              </div></div>
              <div class="FTrf7TqAcfxxEQ_3nfUg"><ul class="b269SZlC2oyR13Fcc4Iy">
                <li id="sl-0"><a href="https://example.com/grain/archive" class="f3uDrYrWF3Exrfp1m3Og"><h3>Archive</h3></a></li>
              </ul></div>
            </article>
          </li>
          <li data-layout="organic">
            <article id="r1-1" data-testid="result" data-nrn="result" class="yQDlj3B5DI5YO8c8Ulio">
              <div class="ikg2IXiCD14iVX7AdZo1"><h2 class="LnpumSThxEWMIsDdAT17">
                <a href="https://example.org/no-snippet" class="eVNpHGjtxRBq_gLOfGDr" data-testid="result-title-a"><span>Nothing to describe</span></a>
              </h2></div>
            </article>
          </li>
          <li data-layout="organic">
            <article id="r1-2" data-testid="result" data-nrn="result" class="yQDlj3B5DI5YO8c8Ulio">
              <div class="ikg2IXiCD14iVX7AdZo1"><h2 class="LnpumSThxEWMIsDdAT17">
                <a href="https://duckduckgo.com/settings" class="eVNpHGjtxRBq_gLOfGDr" data-testid="result-title-a"><span>Settings</span></a>
              </h2></div>
            </article>
          </li>
          <li data-layout="organic">
            <article id="r1-3" data-testid="result" data-nrn="result" class="yQDlj3B5DI5YO8c8Ulio">
              <div class="ikg2IXiCD14iVX7AdZo1"><h2><a href="https://example.net/untitled" data-testid="result-title-a"></a></h2></div>
            </article>
          </li>
        </ol></section></body></html>
        """;

    [Fact]
    public async Task FindAsync_OrdinaryResult_ReadsTitleAddressAndSnippet()
    {
        var first = (await Found(Page))[0];

        Assert.Equal("Grain & barley report", first.Title);
        Assert.Equal("https://example.com/grain", first.Address.Canonical);
        Assert.Equal("Wheat prices rose & barley held steady.", first.Snippet);
    }

    /// <summary>The sitelinks listed under a result are the same site's pages, not results of their own.</summary>
    [Fact]
    public async Task FindAsync_SitelinksUnderAResult_AreNotResults()
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Canonical.Contains("archive", StringComparison.Ordinal));

    [Fact]
    public async Task FindAsync_ResultWithoutADescription_HasAnEmptySnippet()
        => Assert.Equal(string.Empty, (await Found(Page))[1].Snippet);

    [Fact]
    public async Task FindAsync_TheEnginesOwnPages_AreNotResults()
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Address?.Host == "duckduckgo.com");

    [Fact]
    public async Task FindAsync_BlockWithoutATitle_IsSkipped()
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Canonical.Contains("untitled", StringComparison.Ordinal));

    [Fact]
    public async Task FindAsync_EveryResult_NamesTheProvider()
        => Assert.All(await Found(Page), result => Assert.Equal(DuckDuckGo.ProviderName, result.Provider));

    [Fact]
    public async Task FindAsync_PageThatLoaded_ReturnsOnlyTheOutboundResults()
        => Assert.Equal(2, (await Found(Page)).Count);

    [Fact]
    public async Task FindAsync_MarkupItCannotFollow_ReportsNothing()
        => Assert.Empty(await Found("<html><body><div>we redesigned everything</div></body></html>"));

    /// <summary>The name differs from the HTTP provider's, so the search rests and configures the two apart.</summary>
    [Fact]
    public void ProviderName_IsNotTheHttpProvidersName()
        => Assert.NotEqual(Providers.DuckDuckGo.ProviderName, DuckDuckGo.ProviderName);

    [Fact]
    public async Task FindAsync_Query_IsEscapedIntoTheConfiguredDomain()
    {
        var reader = new StubReader(_ => Fixture.Read(Page));

        await Provider(reader, "duckduckgo.example").FindAsync("grain & barley");

        Assert.Equal(
            "https://duckduckgo.example/?q=grain%20%26%20barley&ia=web",
            reader.Requested?.Address?.AbsoluteUri);
    }

    /// <summary>The results arrive by script, so the whole page is read and the reader waits for the first of them.</summary>
    /// <remarks>
    /// Measured on 2026-11-07 against the live site, three queries: no result was in the document at
    /// <c>DOMContentLoaded</c>, and the first appeared 340 to 870 ms after it. The document alone would refuse the
    /// request that carries them, and a page taken without the wait would parse into nothing on a good day.
    /// </remarks>
    [Fact]
    public async Task FindAsync_Query_ReadsTheWholePageAndWaitsForTheFirstResult()
    {
        var reader = new StubReader(_ => Fixture.Read(Page));

        await Provider(reader).FindAsync("grain");

        Assert.Equal(Appetite.Everything, reader.Wanted);
        Assert.Equal("article[data-testid='result']", reader.ReadyWhen);
    }

    private static async Task<IReadOnlyList<SearchResult>> Found(string html)
        => (await Provider(new StubReader(_ => Fixture.Read(html))).FindAsync("grain")).Value;

    private static DuckDuckGo Provider(IPageReader reader, string domain = "duckduckgo.com")
        => new(reader,
            Options.Create(new DuckDuckGoOptions { Domain = domain }),
            Options.Create(new SearchOptions()),
            NullLogger<DuckDuckGo>.Instance);
}
