using Snail.Toolkit.Web.Sitemaps;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the document that turns "read this whole domain" into a list of addresses.
/// </summary>
public class SitemapFileTests
{
    private const string Urlset =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
          <url>
            <loc>https://example.com/grain</loc>
            <lastmod>2026-09-01T10:00:00+00:00</lastmod>
            <changefreq>daily</changefreq>
          </url>
          <url>
            <loc>https://example.com/barley</loc>
          </url>
        </urlset>
        """;

    private const string Index =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
          <sitemap><loc>https://example.com/pages.xml</loc></sitemap>
          <sitemap><loc>https://example.com/news.xml</loc></sitemap>
        </sitemapindex>
        """;

    [Fact]
    public void Parse_Urlset_ReadsEveryPage()
        => Assert.Equal(
            ["https://example.com/grain", "https://example.com/barley"],
            SitemapFile.Parse(Urlset).Pages.Select(page => page.Address.Canonical));

    [Fact]
    public void Parse_PageWithALastModified_ReadsIt()
        => Assert.Equal(
            new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
            SitemapFile.Parse(Urlset).Pages[0].LastModified);

    /// <summary>A page without a date is ordinary; the reader answers unknown rather than inventing one.</summary>
    [Fact]
    public void Parse_PageWithoutALastModified_AnswersUnknown()
        => Assert.Null(SitemapFile.Parse(Urlset).Pages[1].LastModified);

    [Fact]
    public void Parse_Urlset_PointsAtNoFurtherSitemaps()
        => Assert.False(SitemapFile.Parse(Urlset).IsIndex);

    /// <summary>Large sites split their sitemap and publish an index of the parts.</summary>
    [Fact]
    public void Parse_SitemapIndex_ReadsTheSitemapsRatherThanPages()
    {
        var file = SitemapFile.Parse(Index);

        Assert.True(file.IsIndex);
        Assert.Empty(file.Pages);
        Assert.Equal(
            ["https://example.com/pages.xml", "https://example.com/news.xml"],
            file.Sitemaps.Select(sitemap => sitemap.Canonical));
    }

    /// <summary>
    /// Plenty of real sitemaps declare the wrong namespace or none. Refusing those would reject exactly the
    /// sites whose markup already needs the most patience.
    /// </summary>
    [Fact]
    public void Parse_UrlsetWithoutTheSchemaNamespace_IsStillRead()
        => Assert.Single(SitemapFile.Parse("<urlset><url><loc>https://example.com/a</loc></url></urlset>").Pages);

    [Fact]
    public void Parse_EntryWithoutAnAddress_IsDropped()
        => Assert.Empty(SitemapFile.Parse("<urlset><url><lastmod>2026-09-01</lastmod></url></urlset>").Pages);

    [Fact]
    public void Parse_RelativeAddress_IsDropped()
        => Assert.Empty(SitemapFile.Parse("<urlset><url><loc>/grain</loc></url></urlset>").Pages);

    [Fact]
    public void Parse_TheSamePageTwice_IsListedOnce()
        => Assert.Single(SitemapFile.Parse("""
            <urlset>
              <url><loc>https://example.com/grain</loc></url>
              <url><loc>https://example.com/grain/</loc></url>
            </urlset>
            """).Pages);

    /// <summary>
    /// A site's own file is input, not a contract: a sitemap that is actually an error page reads as nothing,
    /// the way an unparseable robots.txt does, and the caller reports that it yielded nothing.
    /// </summary>
    [Theory]
    [InlineData("<html>404 not found")]
    [InlineData("")]
    public void Parse_SomethingThatIsNotASitemap_ReadsAsEmpty(string xml)
    {
        var file = SitemapFile.Parse(xml);

        Assert.Empty(file.Pages);
        Assert.Empty(file.Sitemaps);
    }
}
