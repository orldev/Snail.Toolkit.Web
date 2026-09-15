using HtmlAgilityPack;

namespace Snail.Toolkit.Web.Text;

/// <summary>
/// Parses a page's markup, and refuses markup nested deeper than a page really is.
/// </summary>
/// <remarks>
/// The parser is QUADRATIC IN NESTING DEPTH, and nothing here bounded it. Measured on 2026-09-15 by
/// <c>probes/Markup.cs</c> against HtmlAgilityPack 1.13.0, as the read path pays it — <c>Clean</c> plus
/// <c>Title</c>, both of which every read goes through:
/// <code>
///   depth     12 →     4.7 ms      depth   2000 →    70 ms
///   depth    500 →     5.1 ms      depth   5000 →   400 ms
///   depth   1000 →    16.1 ms      depth  20000 →  5.60 s
/// </code>
/// SIZE IS THE WRONG AXIS, which is what makes this worth a type of its own. A catalogue of 2.3 MB nested
/// twenty deep costs 254 ms; a page of 161 KB nested five thousand deep costs 400. A ceiling on bytes
/// generous enough to admit the first admits the second, and the second is the one that holds a worker for
/// half a second of pure CPU on 161 KB that any site can serve.
/// <para>
/// The ceiling is the parser's own, checked INSIDE the parse rather than after it, and that is the whole
/// reason this is cheap enough to be a cure: refusing costs 1.4 ms where accepting cost 200, and 1.6 ms where
/// accepting cost 2.79 s. On markup that passes it costs nothing measurable — the first version of that
/// measurement ran the two arms one after the other and reported a 20% tax, which was the second arm running
/// on a warmer process; interleaved by round, the difference changes sign between runs.
/// </para>
/// <para>
/// WHY 1000 AND NOT A SETTING. Every real page measured sits two orders below it: an article nests twelve
/// deep, a catalogue twenty. The ceiling is not a policy a consumer tunes to taste — nobody wants a page
/// nested a thousand deep read — it is the line past which markup stops being a document, and putting a
/// number like that in configuration invites it to be raised by someone who has met a refusal rather than a
/// page. What a consumer needs instead is to be TOLD, which is why the refusal is named rather than silent.
/// If a real page is ever found above this, the answer to weigh is a setting, and this paragraph is where the
/// argument against one already stands.
/// </para>
/// <para>
/// THE FOUR SEARCH ENGINES CAME THROUGH HERE ON 2026-09-15, and the question that had kept them out was the
/// one this paragraph named: what a refused results page IS. It is neither of the two answers that were
/// available — not "no results", which is a statement about the query, and not a fault, which is a defect on
/// our side to go and fix — so the search contract grew a third, <c>SearchFailureKind.Unreadable</c>, which
/// rests no engine because markup too deep to read says nothing about how often the engine will answer. The
/// two engines that fetch over HTTP parse here and map the refusal themselves; the two driven by a browser
/// parse in <c>BrowserSearch</c>, which is why that package is named beside the others in Web.csproj.
/// </para>
/// </remarks>
internal static class Markup
{
    /// <summary>How deep a page may nest before it is refused instead of read.</summary>
    internal const int MostNesting = 1000;

    /// <summary>The parsed document, or a refusal naming the depth it stopped at.</summary>
    /// <remarks>
    /// The catch is broad because the parser gives nothing narrower to catch: HtmlAgilityPack 1.13.0 raises a
    /// bare <see cref="Exception"/> for this, so the type cannot tell it apart from anything else and neither
    /// can its message, which is English prose subject to change. What makes the breadth acceptable is what
    /// else <c>LoadHtml</c> can do — it reads a string, touches nothing, and is built to accept malformed
    /// markup rather than to complain about it. <see cref="OutOfMemoryException"/> is let through because it
    /// is a fact about the process rather than about the page, and calling it a nesting depth would send
    /// whoever reads the failure looking at the wrong thing.
    /// </remarks>
    internal static HtmlDocument Read(string html)
    {
        var document = new HtmlDocument { OptionMaxNestedChildNodes = MostNesting };

        try
        {
            document.LoadHtml(html);
        }
        catch (Exception refusal) when (refusal is not OutOfMemoryException)
        {
            throw new MarkupTooDeepException(MostNesting, refusal);
        }

        return document;
    }
}
