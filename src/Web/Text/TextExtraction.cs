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
/// <para>
/// THAT SECOND FIX OVERSHOT, and stood until 2026-10-04: every newline of the SOURCE was kept, as though
/// a line break in markup were a line break on the page. It is whitespace. A sentence written across three
/// source lines with a link in the middle came back as three lines, a navigation bar as a column of single
/// words — measured through the MCP server built on this toolkit, on a page of the PostgreSQL manual, where
/// roughly half the lines of the text were one word long. What ends a line now is what ends one in a
/// browser: a block element, on both of its sides. Whitespace inside a text node is one space, except in
/// preformatted text, which is the one place a line break in the source is the author's.
/// </para>
/// <para>
/// Found by the same reading: the page's <c>title</c> was part of its text, and since it is not a block it
/// ran into the first paragraph — «Example DomainThis domain is for use…». The title is answered by
/// <see cref="Title"/>; here the head is noise like a script is.
/// </para>
/// </remarks>
public sealed partial class TextExtraction : ITextExtraction
{
    private const string NoiseQuery = "//script|//style|//noscript|//iframe|//svg|//template|//head|//title";

    /// <summary>The text whose line breaks are the author's own.</summary>
    private const string VerbatimQuery = "//pre//text()";

    /// <summary>The page's own title first, and only outside an inline drawing, whose title names the drawing.</summary>
    private const string TitleQuery = "//title[not(ancestor::svg)]";

    private const string HeadingQuery = "//h1";

    /// <summary>The elements a browser starts a new line for, before and after.</summary>
    private static readonly HashSet<string> Breaks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main", "nav", "aside", "address",
        "li", "ul", "ol", "dl", "dt", "dd", "tr", "table", "caption", "br", "hr", "pre", "blockquote",
        "figure", "figcaption", "form", "fieldset", "details", "summary", "option",
        "h1", "h2", "h3", "h4", "h5", "h6"
    };

    /// <summary>The elements that stand apart from their neighbours on the same line.</summary>
    private static readonly HashSet<string> Cells = new(StringComparer.OrdinalIgnoreCase) { "td", "th" };

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

    /// <summary>Writes the document's text, with a line break on both sides of every block element.</summary>
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
    /// all been written. It is pushed before that element's children, so it is popped after them. The break
    /// BEFORE the element is written at once: without it, text standing directly ahead of a block —
    /// <c>&lt;div&gt;Introduction&lt;p&gt;First&lt;/p&gt;&lt;/div&gt;</c> — ran into it as «IntroductionFirst».
    /// </para>
    /// </remarks>
    private static void Append(HtmlNode root, StringBuilder text)
    {
        var pending = new Stack<HtmlNode?>();
        var verbatim = root.SelectNodes(VerbatimQuery)?.ToHashSet() ?? [];

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
                text.Append(verbatim.Contains(node) ? node.InnerText : AnySpace().Replace(node.InnerText, " "));
                continue;
            }

            if (node.NodeType is not HtmlNodeType.Element)
                continue;

            if (Breaks.Contains(node.Name))
            {
                text.Append('\n');
                pending.Push(null);
            }
            else if (Cells.Contains(node.Name))
            {
                text.Append(' ');
            }

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
