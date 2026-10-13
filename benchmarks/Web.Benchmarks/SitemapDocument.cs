using System.Globalization;
using System.Text;

namespace Snail.Toolkit.Web.Benchmarks;

/// <summary>A sitemap of the size the audit read: ten thousand addresses, each dated, some with tracking on them.</summary>
/// <remarks>
/// Written here rather than recorded, like the pages: five shapes of address in turn — a folder, a query, a dated
/// path, tracking pairs beside an identifier and a file — each with the date and the change frequency a CMS writes.
/// </remarks>
public static class SitemapDocument
{
    /// <summary>Gets how many addresses the document lists.</summary>
    public const int Addresses = 10_000;

    /// <summary>Builds the document.</summary>
    /// <returns>The XML.</returns>
    public static string Build()
    {
        var text = new StringBuilder(Addresses * 160);

        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");

        for (var index = 0; index < Addresses; index++)
        {
            var path = (index % 5) switch
            {
                0 => $"/articles/{index}/",
                1 => $"/catalogue/item-{index}?ref=sitemap",
                2 => $"/news/2026/10/{index}",
                3 => $"/products/{index}?utm_source=sitemap&amp;id={index}",
                _ => $"/docs/section-{index % 97}/page-{index}.html"
            };

            text.Append(CultureInfo.InvariantCulture, $"  <url>\n    <loc>https://www.example.com{path}</loc>\n");
            text.Append(CultureInfo.InvariantCulture, $"    <lastmod>2026-{1 + index % 12:00}-{1 + index % 28:00}T10:00:00+00:00</lastmod>\n");
            text.Append("    <changefreq>weekly</changefreq>\n  </url>\n");
        }

        return text.Append("</urlset>\n").ToString();
    }
}
