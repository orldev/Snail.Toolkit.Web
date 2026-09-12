using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Snail.Toolkit.Web.Text;

/// <summary>
/// Turns markup into readable text with its paragraph structure intact.
/// </summary>
/// <remarks>
/// Two defects are fixed here against the version this came from. <c>SelectNodes</c> answers
/// <see langword="null"/> when nothing matches, and the result was dereferenced unconditionally, so any
/// document without a script, style, noscript or frame threw <see cref="NullReferenceException"/>. And every
/// newline was collapsed into a space, which destroys the paragraph boundaries a language model needs to tell
/// a heading from a sentence.
/// </remarks>
public sealed partial class TextExtraction : ITextExtraction
{
    private const string NoiseQuery = "//script|//style|//noscript|//iframe|//svg|//template";

    /// <summary>The page's own title first, and only outside an inline drawing, whose title names the drawing.</summary>
    private const string TitleQuery = "//title[not(ancestor::svg)]";

    private const string HeadingQuery = "//h1";

    private static readonly string[] Breaks =
    [
        "p", "div", "section", "article", "header", "footer", "li", "tr", "br",
        "h1", "h2", "h3", "h4", "h5", "h6"
    ];

    /// <inheritdoc />
    public string Clean(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var document = new HtmlDocument();
        document.LoadHtml(html);

        if (document.DocumentNode.SelectNodes(NoiseQuery) is { } noise)
        {
            foreach (var node in noise.ToArray())
                node.Remove();
        }

        var text = new StringBuilder();
        Append(document.DocumentNode, text);

        return Collapse(WebUtility.HtmlDecode(text.ToString()));
    }

    /// <inheritdoc />
    public string? Title(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var document = new HtmlDocument();
        document.LoadHtml(html);

        var named = document.DocumentNode.SelectSingleNode(TitleQuery)
            ?? document.DocumentNode.SelectSingleNode(HeadingQuery);

        if (named is null)
            return null;

        var title = AnySpace().Replace(WebUtility.HtmlDecode(named.InnerText), " ").Trim();

        return title.Length > 0 ? title : null;
    }

    private static void Append(HtmlNode node, StringBuilder text)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType is HtmlNodeType.Text)
            {
                text.Append(child.InnerText);
                continue;
            }

            if (child.NodeType is not HtmlNodeType.Element)
                continue;

            var isBreak = Breaks.Contains(child.Name, StringComparer.OrdinalIgnoreCase);

            Append(child, text);

            if (isBreak)
                text.Append('\n');
        }
    }

    private static string Collapse(string text)
        => BlankLines().Replace(HorizontalSpace().Replace(text, " "), "\n").Trim();

    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex HorizontalSpace();

    [GeneratedRegex(@"\s*\n\s*(?:\n\s*)*")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"\s+")]
    private static partial Regex AnySpace();
}
