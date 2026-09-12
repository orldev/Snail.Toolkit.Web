using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Snail.Toolkit.Web.Sitemaps;

/// <summary>
/// One sitemap document: the pages it lists, or the sitemaps it points at.
/// </summary>
/// <param name="Pages">The pages a <c>urlset</c> lists.</param>
/// <param name="Sitemaps">The sitemaps a <c>sitemapindex</c> points at, each with the date the index gives it.</param>
/// <remarks>
/// A sitemap is why reading a whole domain is usually not a crawl at all: the site hands over the list of its
/// own pages, including the ones no navigation links to, and the reader that already reads a list of addresses
/// does the rest. Following links is the fallback for sites that publish none.
/// <para>
/// Element names are matched without their namespace. The schema says <c>sitemaps.org/schemas/sitemap/0.9</c>,
/// plenty of real sitemaps declare something else or nothing at all, and refusing those would reject exactly
/// the sites whose markup already needs the most patience.
/// </para>
/// <para>
/// Malformed XML reads as an empty document rather than an exception, which matches how <c>robots.txt</c> is
/// read here: a site's own file is input, not a contract, and a caller that gets nothing back reports that the
/// sitemap yielded nothing.
/// </para>
/// </remarks>
public sealed record SitemapFile(IReadOnlyList<SitemapEntry> Pages, IReadOnlyList<SitemapEntry> Sitemaps)
{
    /// <summary>A document that lists nothing, used when a site has no sitemap or an unreadable one.</summary>
    public static SitemapFile Empty { get; } = new([], []);

    /// <summary>Gets whether this document points at further sitemaps rather than at pages.</summary>
    public bool IsIndex => Sitemaps.Count > 0;

    /// <summary>Reads a <c>urlset</c> or <c>sitemapindex</c> document.</summary>
    public static SitemapFile Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        if (Root(xml) is not { } root)
            return Empty;

        List<SitemapEntry> pages = [];
        List<SitemapEntry> sitemaps = [];
        HashSet<Link> seen = [];

        foreach (var element in root.Elements())
        {
            if (Address(element) is not { } address || !seen.Add(address))
                continue;

            if (element.Name.LocalName.Equals("sitemap", StringComparison.OrdinalIgnoreCase))
            {
                sitemaps.Add(new SitemapEntry(address, Modified(element)));
                continue;
            }

            if (element.Name.LocalName.Equals("url", StringComparison.OrdinalIgnoreCase))
                pages.Add(new SitemapEntry(address, Modified(element)));
        }

        return new SitemapFile(pages, sitemaps);
    }

    private static XElement? Root(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Root;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static Link? Address(XElement element)
    {
        if (Child(element, "loc") is not { } text)
            return null;

        var address = Link.Of(text);

        return address.IsAbsolute ? address : null;
    }

    private static DateTimeOffset? Modified(XElement element)
    {
        if (Child(element, "lastmod") is not { } text)
            return null;

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var when)
            ? when
            : null;
    }

    private static string? Child(XElement element, string name)
    {
        var child = element.Elements()
            .FirstOrDefault(candidate => candidate.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

        var value = child?.Value.Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }
}
