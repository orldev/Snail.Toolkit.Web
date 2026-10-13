using System.Buffers;
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
/// <see cref="Title(string)"/>; here the head is noise like a script is.
/// </para>
/// </remarks>
public sealed partial class TextExtraction : ITextExtraction
{
    /// <summary>The page's own title first, and only outside an inline drawing, whose title names the drawing.</summary>
    private const string TitleQuery = "//title[not(ancestor::svg)]";

    private const string HeadingQuery = "//h1";

    /// <summary>The elements whose content is not part of what a reader sees.</summary>
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "script", "style", "noscript", "iframe", "svg", "template", "head", "title"
    };

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
        => string.IsNullOrWhiteSpace(html) ? string.Empty : Clean(Markup.Read(html));

    /// <summary>Answers the readable text of a document already parsed, and leaves the document as it was.</summary>
    /// <remarks>
    /// The text, the title and the links of one page used to be read from a parse each — two for every page read
    /// and four for every page a crawl follows — and parsing is most of what any of them costs: measured on
    /// 2026-10-12 by the audit, the parse alone was 0.28–0.43 of the text and title together. One parse can
    /// answer all of them only if none of them changes it, and the text used to: it removed the scripts, the
    /// styles and the whole head from the document before walking it, and the head is where the canonical
    /// address, the base and the robots directives of the links are written. The walk now passes over those
    /// elements instead of removing them, and answers the same text.
    /// </remarks>
    public string Clean(HtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var text = new StringBuilder();
        Append(document.DocumentNode, text);

        return HasReference(text) ? Collapse(WebUtility.HtmlDecode(text.ToString())) : Collapsed(text);
    }

    /// <inheritdoc />
    public string? Title(string html)
        => string.IsNullOrWhiteSpace(html) ? null : Title(Markup.Read(html));

    /// <summary>Answers the name of a document already parsed, or <see langword="null"/> when it has none.</summary>
    /// <remarks>The same answer as <see cref="Title(string)"/>, without parsing the markup a second time.</remarks>
    public string? Title(HtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

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
    /// <para>
    /// The break owed to a <c>pre</c> also says so, and that is how the walk knows which text is the author's:
    /// everything between the element and its break is inside it, at any depth. The text nodes under a
    /// <c>pre</c> used to be found by an XPath query over the whole document first.
    /// </para>
    /// </remarks>
    private static void Append(HtmlNode root, StringBuilder text)
    {
        var pending = new Stack<(HtmlNode? Node, bool IsVerbatimEnd)>();
        var verbatim = 0;

        Push(root, pending);

        while (pending.TryPop(out var item))
        {
            if (item.Node is not { } node)
            {
                text.Append('\n');
                verbatim -= item.IsVerbatimEnd ? 1 : 0;
                continue;
            }

            if (node.NodeType is HtmlNodeType.Text)
            {
                AppendText(text, node.InnerText, isVerbatim: verbatim > 0);
                continue;
            }

            if (node.NodeType is not HtmlNodeType.Element || Noise.Contains(node.Name))
                continue;

            if (Breaks.Contains(node.Name))
            {
                var isVerbatim = node.Name.Equals("pre", StringComparison.OrdinalIgnoreCase);

                text.Append('\n');
                pending.Push((null, isVerbatim));
                verbatim += isVerbatim ? 1 : 0;
            }
            else if (Cells.Contains(node.Name))
            {
                text.Append(' ');
            }

            Push(node, pending);
        }
    }

    /// <summary>Stacks a node's children so that they come back off it in the order they were written.</summary>
    private static void Push(HtmlNode node, Stack<(HtmlNode? Node, bool IsVerbatimEnd)> pending)
    {
        var children = node.ChildNodes;

        for (var index = children.Count - 1; index >= 0; index--)
            pending.Push((children[index], false));
    }

    /// <summary>Writes a text node: as it stands inside preformatted text, with every run of white space as one space elsewhere.</summary>
    /// <remarks>
    /// The runs were collapsed by a regular expression on every text node of the page, each answering a string of
    /// its own before it was appended; collapsing while appending answers the same characters.
    /// </remarks>
    private static void AppendText(StringBuilder text, string value, bool isVerbatim)
    {
        if (isVerbatim)
        {
            text.Append(value);
            return;
        }

        var span = value.AsSpan();
        var at = 0;

        while (at < span.Length)
        {
            var start = at;

            while (at < span.Length && !char.IsWhiteSpace(span[at]))
                at++;

            text.Append(span[start..at]);

            if (at == span.Length)
                return;

            text.Append(' ');

            while (at < span.Length && char.IsWhiteSpace(span[at]))
                at++;
        }
    }

    /// <summary>Makes every run of white space one line break where it holds one, one space where not, and trims the ends.</summary>
    /// <remarks>
    /// One pass, where there were two regular expressions over the whole text and a trim after them, each
    /// answering a copy of it.
    /// </remarks>
    private static string Collapse(string text) => Collapse(text.AsSpan());

    /// <summary>Answers whether the gathered text holds an ampersand, the only character decoding acts on.</summary>
    private static bool HasReference(StringBuilder text)
    {
        foreach (var chunk in text.GetChunks())
        {
            if (chunk.Span.Contains('&'))
                return true;
        }

        return false;
    }

    /// <summary>Collapses the gathered text straight from the builder, when there is nothing in it to decode.</summary>
    /// <remarks>
    /// Decoding text without an ampersand answers the same text, so the builder's whole length used to be copied
    /// into a string only to be read once more and dropped: two hundred kilobytes on a page of a hundred thousand
    /// characters. It is copied into a pooled buffer instead.
    /// </remarks>
    private static string Collapsed(StringBuilder text)
    {
        var gathered = ArrayPool<char>.Shared.Rent(text.Length);

        try
        {
            text.CopyTo(0, gathered, text.Length);

            return Collapse(gathered.AsSpan(0, text.Length));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(gathered);
        }
    }

    private static string Collapse(ReadOnlySpan<char> text)
    {
        var span = text.Trim();

        if (span.IsEmpty)
            return string.Empty;

        var buffer = ArrayPool<char>.Shared.Rent(span.Length);

        try
        {
            var written = 0;
            var at = 0;

            while (at < span.Length)
            {
                var start = at;

                while (at < span.Length && !char.IsWhiteSpace(span[at]))
                    at++;

                span[start..at].CopyTo(buffer.AsSpan(written));
                written += at - start;

                if (at == span.Length)
                    break;

                var isLineBreak = false;

                for (; char.IsWhiteSpace(span[at]); at++)
                    isLineBreak |= span[at] == '\n';

                buffer[written++] = isLineBreak ? '\n' : ' ';
            }

            return new string(buffer, 0, written);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex AnySpace();
}
