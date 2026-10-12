using HtmlAgilityPack;

namespace Snail.Toolkit.Web.Text;

/// <summary>
/// Reads a document's links as absolute addresses.
/// </summary>
/// <remarks>
/// Everything a page links to is written relative to something, and the something is not always the address the
/// page was fetched from: a <c>&lt;base href&gt;</c> silently moves it, and a reader that ignored that would
/// resolve an entire site one directory off and find nothing.
/// <para>
/// Anything that is not http or https is dropped rather than reported — <c>mailto:</c>, <c>tel:</c>,
/// <c>javascript:</c> and a bare fragment are all ordinary on a real page and none of them is a page to read.
/// Duplicates are dropped by canonical form, so the twenty links a navigation bar repeats count once.
/// </para>
/// <para>
/// <c>nofollow</c> is obeyed, on the link and on the document, because it governs traffic this library is about
/// to send. <c>noindex</c> is not: it asks not to be listed in a search index, and a caller reading one site on
/// purpose is not building one.
/// </para>
/// </remarks>
public sealed class LinkExtraction : ILinkExtraction
{
    /// <inheritdoc />
    public IReadOnlyList<Link> From(string html, Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return Loaded(html) is { } document ? From(document, address) : [];
    }

    /// <summary>Resolves every <c>href</c> a robot may follow in a document already parsed.</summary>
    /// <remarks>
    /// The same links as <see cref="From(string, Uri)"/>, without parsing the markup again: a crawl reads a page's
    /// text, title, links and canonical address, and each used to parse the page for itself.
    /// </remarks>
    public IReadOnlyList<Link> From(HtmlDocument document, Uri address)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(address);

        if (!IsFollowable(document))
            return [];

        if (document.DocumentNode.SelectNodes("//a[@href]") is not { } anchors)
            return [];

        var origin = Origin(document, address);

        List<Link> links = [];
        HashSet<Link> seen = [];

        foreach (var anchor in anchors)
        {
            if (Relations(anchor.GetAttributeValue("rel", string.Empty)).Contains("nofollow"))
                continue;

            if (Resolved(origin, anchor.GetAttributeValue("href", string.Empty)) is not { } link)
                continue;

            if (seen.Add(link))
                links.Add(link);
        }

        return links;
    }

    /// <inheritdoc />
    public Link? CanonicalOf(string html, Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return Loaded(html) is { } document ? CanonicalOf(document, address) : null;
    }

    /// <summary>Reads the address a document already parsed declares as its own, when it declares one.</summary>
    /// <remarks>The same address as <see cref="CanonicalOf(string, Uri)"/>, without parsing the markup again.</remarks>
    public Link? CanonicalOf(HtmlDocument document, Uri address)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(address);

        var declared = document.DocumentNode
            .SelectNodes("//link[@rel][@href]")
            ?.FirstOrDefault(node => Relations(node.GetAttributeValue("rel", string.Empty)).Contains("canonical"));

        if (declared is null)
            return null;

        return Resolved(Origin(document, address), declared.GetAttributeValue("href", string.Empty));
    }

    private static HtmlDocument? Loaded(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        return Markup.Read(html);
    }

    /// <summary>Answers whether the document as a whole allows its links to be followed.</summary>
    private static bool IsFollowable(HtmlDocument document)
    {
        var directives = document.DocumentNode
            .SelectNodes("//meta[@name][@content]")
            ?.Where(node => node.GetAttributeValue("name", string.Empty)
                .Equals("robots", StringComparison.OrdinalIgnoreCase))
            .SelectMany(node => Relations(node.GetAttributeValue("content", string.Empty)));

        return directives is null || !directives.Any(directive => directive is "nofollow" or "none");
    }

    private static IReadOnlyList<string> Relations(string value)
        => [.. value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.ToLowerInvariant())];

    private static Uri Origin(HtmlDocument document, Uri address)
    {
        var declared = document.DocumentNode
            .SelectSingleNode("//base[@href]")
            ?.GetAttributeValue("href", string.Empty);

        if (string.IsNullOrWhiteSpace(declared))
            return address;

        return Uri.TryCreate(address, declared.Trim(), out var origin) ? origin : address;
    }

    private static Link? Resolved(Uri origin, string href)
    {
        var text = HtmlEntity.DeEntitize(href).Trim();

        if (text.Length == 0 || text.StartsWith('#'))
            return null;

        if (!Uri.TryCreate(origin, text, out var absolute))
            return null;

        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            return null;

        return Link.Of(absolute);
    }
}
