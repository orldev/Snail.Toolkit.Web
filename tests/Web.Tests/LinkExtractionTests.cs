using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers reading a page's own links, which is how a domain is read when it publishes no sitemap.
/// </summary>
public class LinkExtractionTests
{
    private static readonly Uri Page = new("https://example.com/docs/guide.html");

    private readonly LinkExtraction _links = new();

    [Fact]
    public void From_RelativeHref_ResolvesAgainstThePage()
        => Assert.Equal(
            ["https://example.com/docs/next.html"],
            Canonicals("""<a href="next.html">next</a>"""));

    [Fact]
    public void From_RootRelativeHref_ResolvesAgainstTheHost()
        => Assert.Equal(["https://example.com/about"], Canonicals("""<a href="/about">about</a>"""));

    /// <summary>A document already parsed answers the links and the canonical address its markup answers.</summary>
    [Fact]
    public void From_ParsedDocument_AnswersWhatTheMarkupAnswers()
    {
        const string html = """
            <head><base href="/docs/"><link rel="canonical alternate" href="guide.html"></head>
            <a href="next.html?utm_source=x">next</a><a rel="nofollow" href="/out">out</a><a href="next.html">again</a>
            """;
        var document = Markup.Read(html);

        Assert.Equal(_links.From(html, Page), _links.From(document, Page));
        Assert.Equal(_links.CanonicalOf(html, Page), _links.CanonicalOf(document, Page));
    }

    /// <summary>
    /// A link resolved from the page carries the address its text parses into, not the resolution it came from:
    /// the two differ in what they remember being written, and the address is public.
    /// </summary>
    [Fact]
    public void From_HrefWrittenWithoutItsSlash_CarriesTheAddressItsTextParsesInto()
    {
        var link = Assert.Single(_links.From("""<a href="https://www.example.org">home</a>""", Page));

        Assert.Equal("https://www.example.org/", link.Address!.OriginalString);
        Assert.Equal("https://www.example.org/", link.Text);
    }

    /// <summary>
    /// A <c>&lt;base href&gt;</c> silently moves what every relative link means. Ignoring it resolves a whole
    /// site one directory off, and every address then answers 404.
    /// </summary>
    [Fact]
    public void From_PageDeclaringABase_ResolvesAgainstThatBase()
        => Assert.Equal(
            ["https://cdn.example.com/next.html"],
            Canonicals("""<base href="https://cdn.example.com/"><a href="next.html">next</a>"""));

    [Theory]
    [InlineData("""<a href="mailto:someone@example.com">mail</a>""")]
    [InlineData("""<a href="tel:+1234">call</a>""")]
    [InlineData("""<a href="javascript:void(0)">menu</a>""")]
    [InlineData("""<a href="#section">jump</a>""")]
    [InlineData("""<a href="">nothing</a>""")]
    public void From_HrefThatIsNotAPage_IsDropped(string html)
        => Assert.Empty(Canonicals(html));

    /// <summary>A navigation bar repeats the same twenty links on every page; they are one link each.</summary>
    [Fact]
    public void From_TheSameAddressInSeveralShapes_IsListedOnce()
        => Assert.Single(Canonicals("""
            <a href="/about">about</a>
            <a href="https://example.com/about/">about</a>
            <a href="/about#team">team</a>
            """));

    [Fact]
    public void From_EscapedQueryString_ReadsTheRealAddress()
        => Assert.Equal(
            ["https://example.com/search?q=grain&page=2"],
            Canonicals("""<a href="/search?q=grain&amp;page=2">more</a>"""));

    [Fact]
    public void From_LinksInOrder_KeepsThatOrder()
        => Assert.Equal(
            ["https://example.com/a", "https://example.com/b"],
            Canonicals("""<a href="/a">a</a><a href="/b">b</a>"""));

    [Fact]
    public void From_DocumentWithoutLinks_ReportsNothing()
        => Assert.Empty(Canonicals("<p>Wheat prices rose.</p>"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_EmptyDocument_ReportsNothing(string html)
        => Assert.Empty(Canonicals(html));

    /// <summary>
    /// Obeyed because it governs traffic this library is about to send, on the link and on the document alike.
    /// </summary>
    [Fact]
    public void From_LinkMarkedNofollow_IsNotFollowed()
        => Assert.Equal(
            ["https://example.com/keep"],
            Canonicals("""<a href="/keep">keep</a><a href="/skip" rel="nofollow noopener">skip</a>"""));

    [Fact]
    public void From_DocumentMarkedNofollow_LeadsNowhere()
        => Assert.Empty(Canonicals("""<meta name="robots" content="noindex, nofollow"><a href="/a">a</a>"""));

    /// <summary>A page asking not to be indexed is still a page a caller asked to read.</summary>
    [Fact]
    public void From_DocumentMarkedNoindexOnly_IsStillFollowed()
        => Assert.Single(Canonicals("""<meta name="robots" content="noindex"><a href="/a">a</a>"""));

    /// <summary>
    /// The one thing that can say two different addresses are the same document. Without it a crawl reports a
    /// print view, a page parameter and the article itself as three pages.
    /// </summary>
    [Fact]
    public void CanonicalOf_DocumentClaimingAnAddress_ReadsIt()
        => Assert.Equal(
            "https://example.com/docs/guide",
            _links.CanonicalOf("""<link rel="canonical" href="/docs/guide">""", Page)?.Canonical);

    [Fact]
    public void CanonicalOf_DocumentClaimingNothing_AnswersUnknown()
        => Assert.Null(_links.CanonicalOf("<p>Wheat prices rose.</p>", Page));

    private IEnumerable<string> Canonicals(string html)
        => _links.From(html, Page).Select(link => link.Canonical);
}
