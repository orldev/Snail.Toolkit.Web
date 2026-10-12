using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers which road is handed the parsed document: a crawl reads its links from it, everyone else never sees it.
/// </summary>
[Trait("Category", "Unit")]
public class PageContentsTests
{
    private const string Html = """
        <html><head><title>Grain</title><link rel="canonical" href="/grain"></head>
        <body><p>Wheat and barley.</p><a href="/barley">barley</a><a rel="nofollow" href="/out">out</a></body></html>
        """;

    private static readonly Uri Address = new("https://example.com/a");

    /// <summary>A consumer may keep every page it reads; a parsed document kept with each would be twenty times the markup.</summary>
    [Fact]
    public void Judged_PlainRead_HandsNoDocumentOn()
        => Assert.Null(Contents(new TextExtraction()).Judged(Page(Html)).Value.Parsed);

    [Fact]
    public void Judged_ReadForACrawl_HandsOnTheDocumentTheLinksAreReadFrom()
    {
        var links = new LinkExtraction();

        var page = Contents(new TextExtraction()).Judged(Page(Html), Reading.TextAndDocument).Value;

        Assert.NotNull(page.Parsed);
        Assert.Equal(links.From(Html, Address), links.From(page.Parsed, Address));
        Assert.Equal(links.CanonicalOf(Html, Address), links.CanonicalOf(page.Parsed, Address));
        Assert.Equal("Grain", page.Title);
    }

    /// <summary>A results page keeps the refusal of markup too deep to read, and costs a parse and nothing more.</summary>
    [Fact]
    public void Judged_ReadForAnEngine_HandsOnTheDocumentWithoutTheText()
    {
        var page = Contents(new TextExtraction()).Judged(Page(Html), Reading.DocumentOnly).Value;

        Assert.NotNull(page.Parsed);
        Assert.Equal(string.Empty, page.Text);
        Assert.Null(page.Title);
    }

    /// <summary>The document is this library's parse, so an extraction of the consumer's own parses nothing for it.</summary>
    [Fact]
    public void Judged_ReadForACrawlWithAnExtractionOfItsOwn_HandsNoDocumentOn()
        => Assert.Null(Contents(new Verbatim()).Judged(Page(Html), Reading.TextAndDocument).Value.Parsed);

    private static PageContents Contents(ITextExtraction text) => new(text, Options.Create(new ScrapingOptions()));

    private static Document Page(string html) => new()
    {
        Address = Link.Of(Address.AbsoluteUri),
        Identity = new WebIdentity
        {
            UserAgent = "Agent/1",
            Platform = "Windows",
            NavigatorPlatform = "Win32",
            Brand = "Google Chrome",
            MajorVersion = "124",
            Locale = "en-US",
            Timezone = "UTC",
            AcceptLanguage = "en-US,en;q=0.9",
            Languages = ["en-US", "en"]
        },
        Html = html,
        Rendered = "Wheat and barley.",
        FinalUrl = Address.AbsoluteUri,
        StatusCode = 200
    };

    private sealed class Verbatim : ITextExtraction
    {
        public string Clean(string html) => html;

        public string? Title(string html) => null;
    }
}
