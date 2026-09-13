using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// Covers the one thing Google contributes over <see cref="BrowserSearch"/>: its address and its markup.
/// </summary>
public class GoogleTests
{
    private const string Page =
        """
        <html><body><div id="search">
          <div data-hveid="a1">
            <a href="https://example.com/grain"><h3>Grain &amp; barley report</h3></a>
            <div data-sncf="1">Wheat prices rose &amp; barley held steady.</div>
          </div>
          <div data-hveid="a2">
            <a href="/url?q=https%3A%2F%2Fwrapped.example%2Fpage&amp;sa=U"><h3>Wrapped result</h3></a>
          </div>
          <div data-hveid="a3">
            <a href="https://accounts.google.com/ServiceLogin"><h3>Sign in</h3></a>
          </div>
          <div data-hveid="a4">
            <a href="https://www.google.com/search?q=more"><h3>More results</h3></a>
          </div>
          <div data-hveid="a5">
            <a href="https://developers.google.com/search/docs"><h3>Google Search Central</h3></a>
          </div>
          <div data-hveid="a6">
            <a href="https://example.com/no-heading">no heading here</a>
          </div>
        </div></body></html>
        """;

    /// <summary>Anchored on a link containing a heading, which is the one shape that survives layout churn.</summary>
    [Fact]
    public async Task FindAsync_OrdinaryResult_ReadsTitleAddressAndSnippet()
    {
        var first = (await Found(Page))[0];

        Assert.Equal("Grain & barley report", first.Title);
        Assert.Equal("https://example.com/grain", first.Address.Canonical);
        Assert.Equal("Wheat prices rose & barley held steady.", first.Snippet);
    }

    [Fact]
    public async Task FindAsync_WrappedLink_UnwrapsTheRealAddress()
        => Assert.Equal("https://wrapped.example/page", (await Found(Page))[1].Address.Canonical);

    /// <summary>Sign-in, policies, help and the search host itself are the page's furniture, not answers.</summary>
    [Theory]
    [InlineData("accounts.google.com")]
    [InlineData("www.google.com")]
    public async Task FindAsync_GooglesOwnFurniture_IsNotAResult(string host)
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Address?.Host == host);

    /// <summary>
    /// A result that genuinely lives on a Google domain is still a result: excluding everything Google-shaped
    /// would drop real answers to real queries.
    /// </summary>
    [Fact]
    public async Task FindAsync_GenuineResultOnAGoogleDomain_IsKept()
        => Assert.Contains(await Found(Page), result => result.Address.Canonical == "https://developers.google.com/search/docs");

    [Fact]
    public async Task FindAsync_LinkWithoutAHeading_IsSkipped()
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Canonical.Contains("no-heading", StringComparison.Ordinal));

    /// <summary>Image, video and knowledge blocks carry a link and a heading with no prose at all.</summary>
    [Fact]
    public async Task FindAsync_ResultWithoutASnippet_ReportsAnEmptyOne()
        => Assert.Equal(string.Empty, (await Found(Page))[1].Snippet);

    [Fact]
    public async Task FindAsync_EveryResult_NamesTheProvider()
        => Assert.All(await Found(Page), result => Assert.Equal(Google.ProviderName, result.Provider));

    [Fact]
    public async Task FindAsync_PageThatLoaded_ReturnsOnlyTheOutboundLinks()
        => Assert.Equal(3, (await Found(Page)).Count);

    [Fact]
    public async Task FindAsync_MarkupItCannotFollow_ReportsNothing()
        => Assert.Empty(await Found("<html><body><div>we redesigned everything</div></body></html>"));

    [Fact]
    public async Task FindAsync_Query_IsEscapedIntoTheConfiguredDomain()
    {
        var reader = new StubReader(_ => Fixture.Read(Page));

        await Provider(reader, "www.google.de").FindAsync("grain & barley");

        Assert.Equal(
            "https://www.google.de/search?q=grain%20%26%20barley",
            reader.Requested?.Address?.AbsoluteUri);
    }

    private static async Task<IReadOnlyList<SearchResult>> Found(string html)
        => (await Provider(new StubReader(_ => Fixture.Read(html))).FindAsync("grain")).Value;

    private static Google Provider(IPageReader reader, string domain = "www.google.com")
        => new(reader,
            Options.Create(new GoogleOptions { Domain = domain }),
            Options.Create(new SearchOptions()),
            NullLogger<Google>.Instance);
}
