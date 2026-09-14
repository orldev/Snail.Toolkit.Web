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

        var document = Markup.Read(html);

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

        var document = Markup.Read(html);

        var named = document.DocumentNode.SelectSingleNode(TitleQuery)
            ?? document.DocumentNode.SelectSingleNode(HeadingQuery);

        if (named is null)
            return null;

        var title = AnySpace().Replace(WebUtility.HtmlDecode(named.InnerText), " ").Trim();

        return title.Length > 0 ? title : null;
    }

    /// <summary>Writes the document's text, with a line break after every block element.</summary>
    /// <remarks>
    /// An explicit stack rather than the call stack, and that is not a matter of taste. The walk used to
    /// recurse once per level of nesting, and nothing bounds how deep a document goes: HtmlAgilityPack builds
    /// whatever the bytes describe. MEASURED on 2026-09-15 against HtmlAgilityPack 1.13.0, on a thread
    /// carrying the 1 MB stack the thread pool gives its workers — which is where a crawl runs every read,
    /// through <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource},ParallelOptions,Func{TSource,CancellationToken,ValueTask})"/>
    /// — six thousand levels returned and eight thousand ended the process with a
    /// <c>StackOverflowException</c>.
    /// <para>
    /// Eight thousand <c>div</c> elements is forty kilobytes of body, which any site can serve and some
    /// generate by accident. That exception cannot be caught, runs no <c>finally</c> and is not a failed
    /// page: it takes the process and every other page in flight with it, which is the one failure in this
    /// library that no amount of retrying survives.
    /// </para>
    /// <para>
    /// A <see langword="null"/> on the stack is the line break owed to a block element whose children have
    /// all been written. It is pushed before that element's children, so it is popped after them.
    /// </para>
    /// </remarks>
    private static void Append(HtmlNode root, StringBuilder text)
    {
        var pending = new Stack<HtmlNode?>();

        Push(root, pending);

        while (pending.TryPop(out var node))
        {
            if (node is null)
            {
                text.Append('\n');
                continue;
            }

            if (node.NodeType is HtmlNodeType.Text)
            {
                text.Append(node.InnerText);
                continue;
            }

            if (node.NodeType is not HtmlNodeType.Element)
                continue;

            if (Breaks.Contains(node.Name, StringComparer.OrdinalIgnoreCase))
                pending.Push(null);

            Push(node, pending);
        }
    }

    /// <summary>Stacks a node's children so that they come back off it in the order they were written.</summary>
    private static void Push(HtmlNode node, Stack<HtmlNode?> pending)
    {
        for (var index = node.ChildNodes.Count - 1; index >= 0; index--)
            pending.Push(node.ChildNodes[index]);
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
