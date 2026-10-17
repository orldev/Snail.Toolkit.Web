using System.Diagnostics;
using System.Globalization;
using HtmlAgilityPack;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Probes;

/// <summary>
/// Measures what parsing a page's markup costs, and how much of that is paid more than once.
/// </summary>
/// <remarks>
/// Reading one page runs the markup through HtmlAgilityPack twice — <c>Clean</c> and <c>Title</c> each build
/// their own <see cref="HtmlDocument"/>, and both are called from the one tail every read goes through. A page
/// reached by following links is parsed twice more, by <c>From</c> and <c>CanonicalOf</c>. Merging each pair
/// into one call would save exactly one parse per pair, and the question this probe exists to answer is
/// whether that is worth changing a contract for.
/// <para>
/// The arms are built rather than fetched, so this touches no site and can be run as often as it is doubted.
/// Each shape states the two things that decide the cost — how much markup there is and how deep it goes —
/// because the second is the one that misbehaves: the parser is quadratic in depth, so a page can be small and
/// still expensive.
/// </para>
/// <para>
/// A warm-up runs before the first arm. Without it the first shape pays for JIT and for the regular expressions
/// these extractions build once, and every later shape is then read against a base that includes neither. That
/// mistake was made in <see cref="Contexts"/> and cost a whole run.
/// </para>
/// <para>
/// WHAT TO READ. The saving column is what merging a pair would buy, and it is one parse — no more. Read it
/// beside the page-load times in the crawl, not on its own: a millisecond saved against a network round trip
/// of two hundred is a millisecond, and this probe is meant to be able to say so.
/// </para>
/// </remarks>
internal static class Markup
{
    private const int Rounds = 7;

    private static readonly Shape[] Shapes =
    [
        new("article", Paragraphs: 400, Depth: 12),
        new("large page", Paragraphs: 3_000, Depth: 20),
        new("catalogue", Paragraphs: 12_000, Depth: 20),
        new("nesting 500", Paragraphs: 200, Depth: 500),
        new("nesting 1000", Paragraphs: 200, Depth: 1_000),
        new("nesting 2000", Paragraphs: 200, Depth: 2_000),
        new("nesting 5000", Paragraphs: 200, Depth: 5_000),
        new("nesting 20000", Paragraphs: 200, Depth: 20_000)
    ];

    /// <summary>The markup <see cref="Page"/> repeats: the head, then a paragraph with <c>{n}</c> for its number.</summary>
    private static readonly string[] Sample = Samples.Lines("markup.html");

    public static Task ReportAsync(string endpoint)
    {
        _ = endpoint;

        var text = new TextExtraction();
        var links = new LinkExtraction();
        var address = new Uri("https://example.test/page");

        Warm(text, links, address);

        Console.WriteLine($"{DateTime.Now:HH:mm} — what parsing markup costs and how much of it is paid twice");
        Console.WriteLine($"  HtmlAgilityPack, {Rounds} rounds per arm, the median is taken");
        Console.WriteLine();
        Console.WriteLine("  page                   size     depth      parse    Clean   Title   reading path     saving");

        foreach (var shape in Shapes)
            Measure(shape, text, links, address);

        Console.WriteLine();
        Console.WriteLine("  'parse' is one LoadHtml without extraction. 'reading path' is Clean + Title, that is");
        Console.WriteLine("  what EVERY page read pays. 'saving' is how much merging the pair into one call");
        Console.WriteLine("  would remove, and that is exactly one parse, no more.");
        Console.WriteLine("  Following links pays as much again a second time: From and CanonicalOf.");

        Refused();

        return Task.CompletedTask;
    }

    /// <summary>Asks what the parser's own depth limit costs, which decides whether a cure can be cheap.</summary>
    /// <remarks>
    /// A ceiling is only a cure if refusing is cheaper than accepting. <c>OptionMaxNestedChildNodes</c> is
    /// checked inside the parse, so the question is whether it stops at the limit or pays the whole quadratic
    /// cost and complains afterwards — and those two answers lead to opposite designs.
    /// <para>
    /// Interleaved rather than one arm after the other: run back to back, the second arm is measured on a warmer
    /// process than the first and the difference between them is read as a cost it is not.
    /// </para>
    /// </remarks>
    private static void Refused()
    {
        Console.WriteLine();
        Console.WriteLine("  the parser's own depth ceiling (OptionMaxNestedChildNodes = 1000)");
        Console.WriteLine();
        Console.WriteLine("  page                 no ceiling   with ceiling   how it ended");

        foreach (var shape in Shapes)
        {
            var html = Page(shape);
            var ended = "parsed";

            var (open, capped) = Paired(
                () => Parse(html),
                () =>
                {
                    try
                    {
                        var document = new HtmlDocument { OptionMaxNestedChildNodes = 1000 };

                        document.LoadHtml(html);

                        _ = document.DocumentNode.Name;
                    }
                    catch (Exception refusal)
                    {
                        ended = $"{refusal.GetType().FullName}: {refusal.Message}";
                    }
                });

            Console.WriteLine($"  {shape.Name,-18} {Ms(open),12} {Ms(capped),12}   {ended}");
        }

        Console.WriteLine();
        Console.WriteLine("  The ceiling is worth it only if refusing is CHEAPER than parsing. If both columns are equal,");
        Console.WriteLine("  the parser pays everything and complains afterwards, and the cure belongs elsewhere.");
        Console.WriteLine("  The arms alternate by round; otherwise the second would be read on a warmer process");
        Console.WriteLine("  and the difference between them would be the warm-up, not the price of the ceiling.");
    }

    /// <summary>Runs every path once on a small page, so the first shape is not the one paying for start-up.</summary>
    private static void Warm(TextExtraction text, LinkExtraction links, Uri address)
    {
        var html = Page(new Shape("warm-up", 50, 5));

        for (var round = 0; round < 3; round++)
        {
            Parse(html);
            text.Clean(html);
            text.Title(html);
            links.From(html, address);
            links.CanonicalOf(html, address);
        }
    }

    private static void Measure(Shape shape, TextExtraction text, LinkExtraction links, Uri address)
    {
        var html = Page(shape);

        var parse = Median(() => Parse(html));
        var clean = Median(() => text.Clean(html));
        var title = Median(() => text.Title(html));

        _ = links.From(html, address);
        _ = links.CanonicalOf(html, address);

        Console.WriteLine(
            $"  {shape.Name,-18} {Size(html),8} {shape.Depth,9} {Ms(parse),10} {Ms(clean),8} {Ms(title),7} "
            + $"{Ms(clean + title),14} {Ms(parse),10}");
    }

    /// <summary>Two arms measured round by round in turn, so neither is the one that ran on a cold process.</summary>
    private static (double First, double Second) Paired(Action first, Action second)
    {
        var ofFirst = new double[Rounds];
        var ofSecond = new double[Rounds];

        for (var round = 0; round < Rounds; round++)
        {
            ofFirst[round] = Taken(first);
            ofSecond[round] = Taken(second);
        }

        Array.Sort(ofFirst);
        Array.Sort(ofSecond);

        return (ofFirst[Rounds / 2], ofSecond[Rounds / 2]);
    }

    private static double Taken(Action work)
    {
        var started = Stopwatch.GetTimestamp();

        work();

        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    /// <summary>The middle of <see cref="Rounds"/> runs, which is what a page costs rather than what it can cost.</summary>
    private static double Median(Action work)
    {
        var taken = new double[Rounds];

        for (var round = 0; round < Rounds; round++)
        {
            var started = Stopwatch.GetTimestamp();

            work();

            taken[round] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        Array.Sort(taken);

        return taken[Rounds / 2];
    }

    private static void Parse(string html)
    {
        var document = new HtmlDocument();

        document.LoadHtml(html);

        _ = document.DocumentNode.Name;
    }

    /// <summary>A page of the stated shape: a nest of that depth, then that many paragraphs inside it.</summary>
    /// <remarks>
    /// Links and a canonical address are included because two of the four parses look for exactly those, and a
    /// page without them would let those calls return early against markup they never walked.
    /// <para>
    /// The markup is kept in <c>Pages/markup.html</c>: the head on its first line, a paragraph with <c>{n}</c> for its
    /// number on the second. Its text is in the language the page was measured in, so it is data, not code.
    /// </para>
    /// </remarks>
    private static string Page(Shape shape)
    {
        var html = new System.Text.StringBuilder();

        html.Append(Sample[0]);

        for (var level = 0; level < shape.Depth; level++)
            html.Append("<div class=\"level\">");

        for (var paragraph = 0; paragraph < shape.Paragraphs; paragraph++)
        {
            html.Append(Sample[1].Replace("{n}", paragraph.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }

        for (var level = 0; level < shape.Depth; level++)
            html.Append("</div>");

        html.Append("</body></html>");

        return html.ToString();
    }

    private static string Size(string html)
    {
        var bytes = System.Text.Encoding.UTF8.GetByteCount(html);

        return bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024.0):0.0} MB"
            : $"{bytes / 1024.0:0} KB";
    }

    private static string Ms(double milliseconds)
        => milliseconds >= 1000 ? $"{milliseconds / 1000:0.00} s" : $"{milliseconds:0.0} ms";

    private sealed record Shape(string Name, int Paragraphs, int Depth);
}
