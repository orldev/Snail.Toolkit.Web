using System.Globalization;
using System.Text;
using System.Xml;

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

    /// <summary>The settings <c>XDocument.Parse</c> reads with when told nothing.</summary>
    private static XmlReaderSettings Settings { get; } = new()
    {
        IgnoreWhitespace = true,
        DtdProcessing = DtdProcessing.Parse,
        MaxCharactersFromEntities = 10_000_000
    };

    /// <summary>Gets whether this document points at further sitemaps rather than at pages.</summary>
    public bool IsIndex => Sitemaps.Count > 0;

    /// <summary>Reads a <c>urlset</c> or <c>sitemapindex</c> document.</summary>
    public static SitemapFile Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var text = new StringReader(xml);

        return Read(text);
    }

    /// <summary>Reads a <c>urlset</c> or <c>sitemapindex</c> document as it streams in.</summary>
    /// <remarks>
    /// Streamed rather than loaded: the document used to be built whole as an <c>XDocument</c>, every element and
    /// every text node of ten thousand addresses held at once, to be walked a single time. The reader is made
    /// with the settings <c>XDocument.Parse</c> uses, and it is read to the end before anything is answered, so
    /// a document that would not have loaded still reads as empty — however late in it the fault lies.
    /// </remarks>
    internal static SitemapFile Read(TextReader text)
    {
        List<SitemapEntry> pages = [];
        List<SitemapEntry> sitemaps = [];
        HashSet<Link> seen = [];

        try
        {
            using var reader = XmlReader.Create(text, Settings);

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1)
                    continue;

                var kind = reader.LocalName;
                var (location, modified) = Fields(reader);

                if (Address(location) is not { } address || !seen.Add(address))
                    continue;

                if (kind.Equals("sitemap", StringComparison.OrdinalIgnoreCase))
                {
                    sitemaps.Add(new SitemapEntry(address, Modified(modified)));
                    continue;
                }

                if (kind.Equals("url", StringComparison.OrdinalIgnoreCase))
                    pages.Add(new SitemapEntry(address, Modified(modified)));
            }
        }
        catch (XmlException)
        {
            return Empty;
        }

        return new SitemapFile(pages, sitemaps);
    }

    /// <summary>The text of an entry's first <c>loc</c> and first <c>lastmod</c>, leaving the reader on its end.</summary>
    private static (string? Location, string? Modified) Fields(XmlReader reader)
    {
        string? location = null;
        string? modified = null;
        var isLocationRead = false;
        var isModifiedRead = false;

        if (reader.IsEmptyElement)
            return (location, modified);

        while (reader.Read() && reader.Depth > 1)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 2)
                continue;

            if (!isLocationRead && reader.LocalName.Equals("loc", StringComparison.OrdinalIgnoreCase))
            {
                isLocationRead = true;
                location = Value(reader);
                continue;
            }

            if (!isModifiedRead && reader.LocalName.Equals("lastmod", StringComparison.OrdinalIgnoreCase))
            {
                isModifiedRead = true;
                modified = Value(reader);
            }
        }

        return (location, modified);
    }

    /// <summary>An element's text, its descendants' included and trimmed, as <c>XElement.Value</c> gave it.</summary>
    private static string? Value(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return null;

        var depth = reader.Depth;
        string? first = null;
        StringBuilder? joined = null;

        while (reader.Read() && reader.Depth > depth)
        {
            if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace))
                continue;

            if (first is null)
                first = reader.Value;
            else
                (joined ??= new StringBuilder(first)).Append(reader.Value);
        }

        var value = (joined?.ToString() ?? first)?.Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static Link? Address(string? text)
    {
        if (text is null)
            return null;

        var address = Link.Of(text);

        return address.IsAbsolute ? address : null;
    }

    private static DateTimeOffset? Modified(string? text)
    {
        if (text is null)
            return null;

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var when)
            ? when
            : null;
    }
}
