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

        var marks = Marks.Of(document, isAnchorsWanted: true);

        if (!marks.IsFollowable || marks.Anchors.Count == 0)
            return [];

        var origin = Origin(marks.Base, address);

        List<Link> links = [];
        HashSet<Link> seen = [];

        foreach (var anchor in marks.Anchors)
        {
            if (Names(anchor.GetAttributeValue("rel", string.Empty), "nofollow"))
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

        var marks = Marks.Of(document, isAnchorsWanted: false);

        if (marks.Canonical is not { } declared)
            return null;

        return Resolved(Origin(marks.Base, address), declared.GetAttributeValue("href", string.Empty));
    }

    private static HtmlDocument? Loaded(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        return Markup.Read(html);
    }

    /// <summary>Answers whether a space- or comma-separated list of relations names <paramref name="relation"/>.</summary>
    /// <remarks>
    /// Read in place: the list used to be split, each entry lower-cased into a string of its own and gathered into
    /// a list, for every anchor of every page, to answer one yes or no. Each entry is still trimmed of white space
    /// and compared after lowering each character by the invariant culture, which is what
    /// <see cref="string.ToLowerInvariant()"/> did to it, so a relation matches exactly the spellings it matched
    /// then. A comparison ignoring case would not: measured over every character on 2026-10-13, lowering turns the
    /// Kelvin sign into <c>k</c> and an ordinal comparison ignoring case does not match the two.
    /// </remarks>
    private static bool Names(string value, string relation)
    {
        var rest = value.AsSpan();

        while (true)
        {
            var cut = rest.IndexOfAny(' ', ',');
            var entry = cut < 0 ? rest : rest[..cut];

            if (IsSpelled(entry.Trim(), relation))
                return true;

            if (cut < 0)
                return false;

            rest = rest[(cut + 1)..];
        }
    }

    private static bool IsSpelled(ReadOnlySpan<char> entry, string relation)
    {
        if (entry.Length != relation.Length)
            return false;

        for (var at = 0; at < entry.Length; at++)
        {
            if (char.ToLowerInvariant(entry[at]) != relation[at])
                return false;
        }

        return true;
    }

    private static Uri Origin(HtmlNode? declaring, Uri address)
    {
        var declared = declaring?.GetAttributeValue("href", string.Empty);

        if (string.IsNullOrWhiteSpace(declared))
            return address;

        return Uri.TryCreate(address, declared.Trim(), out var origin) ? origin : address;
    }

    private static Link? Resolved(Uri origin, string href)
    {
        var text = (href.Contains('&', StringComparison.Ordinal) ? HtmlEntity.DeEntitize(href) : href).Trim();

        if (text.Length == 0 || text.StartsWith('#'))
            return null;

        if (!Uri.TryCreate(origin, text, out var absolute))
            return null;

        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            return null;

        return Link.Of(absolute);
    }

    /// <summary>What a document says about its links, read in one walk: its anchors, its base, its canonical address.</summary>
    /// <remarks>
    /// The walk replaces four XPath queries — the anchors, the robots directives, the base and the canonical link —
    /// each of which went over the whole document on its own. It moves exactly as HtmlAgilityPack's XPath navigator
    /// does, to the first child, the next sibling and back up to the parent, so it meets the same elements in the
    /// same order; an element is matched by its lower-cased name and an attribute by the presence of its lower-cased
    /// name, which is what <c>//a[@href]</c> asked. A prefixed name such as <c>svg:a</c> matched none of the
    /// queries and matches nothing here.
    /// </remarks>
    private sealed class Marks
    {
        public List<HtmlNode> Anchors { get; } = [];

        public bool IsFollowable { get; private set; } = true;

        public HtmlNode? Base { get; private set; }

        public HtmlNode? Canonical { get; private set; }

        public static Marks Of(HtmlDocument document, bool isAnchorsWanted)
        {
            var marks = new Marks();
            var root = document.DocumentNode;
            var node = root.HasChildNodes ? root.ChildNodes[0] : null;
            var depth = 1;

            while (node is not null)
            {
                if (node.NodeType == HtmlNodeType.Element)
                    marks.Note(node, isAnchorsWanted);

                node = Next(node, ref depth);
            }

            return marks;
        }

        private static HtmlNode? Next(HtmlNode node, ref int depth)
        {
            if (node.HasChildNodes)
            {
                depth++;

                return node.ChildNodes[0];
            }

            var current = node;

            while (depth > 0 && current is not null)
            {
                if (current.NextSibling is { } sibling)
                    return sibling;

                current = current.ParentNode;
                depth--;
            }

            return null;
        }

        private void Note(HtmlNode element, bool isAnchorsWanted)
        {
            switch (element.Name)
            {
                case "a" when isAnchorsWanted && Has(element, "href"):
                    Anchors.Add(element);
                    break;
                case "meta" when IsFollowable && Has(element, "name") && Has(element, "content"):
                    IsFollowable = !IsRefusal(element);
                    break;
                case "base" when Base is null && Has(element, "href"):
                    Base = element;
                    break;
                case "link" when Canonical is null && Has(element, "rel") && Has(element, "href"):
                    Canonical = Names(element.GetAttributeValue("rel", string.Empty), "canonical") ? element : null;
                    break;
            }
        }

        private static bool IsRefusal(HtmlNode meta)
        {
            if (!meta.GetAttributeValue("name", string.Empty).Equals("robots", StringComparison.OrdinalIgnoreCase))
                return false;

            var content = meta.GetAttributeValue("content", string.Empty);

            return Names(content, "nofollow") || Names(content, "none");
        }

        private static bool Has(HtmlNode element, string name)
        {
            if (!element.HasAttributes)
                return false;

            var attributes = element.Attributes;

            for (var index = 0; index < attributes.Count; index++)
            {
                if (string.Equals(attributes[index].Name, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
