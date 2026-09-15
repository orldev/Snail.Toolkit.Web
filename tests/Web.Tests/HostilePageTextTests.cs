using System.Diagnostics;
using System.Text;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Drives the text layer with the documents a real crawl meets: written by someone else, to no standard.
/// </summary>
/// <remarks>
/// Everything here is reachable from one fetched page. The budget and the extractor are the last two things
/// that touch a document before it reaches a caller — usually a language model — and both currently assume
/// the page is at least trying to be a page.
/// </remarks>
public class HostilePageTextTests
{
    /// <summary>A cut lands between characters, never inside one.</summary>
    /// <remarks>
    /// <c>said[..MaxCharacters]</c> indexes UTF-16 code units, and every emoji, every musical symbol and
    /// every rarer CJK ideograph is two of them. A ceiling that falls between the pair keeps the high
    /// surrogate and drops the low one, which is not a character at all.
    /// <para>
    /// The consequence is not cosmetic. A lone surrogate cannot be encoded as UTF-8: a lenient encoder
    /// silently substitutes <c>U+FFFD</c> and a strict one — which is what a transport, a strict JSON writer
    /// or a database driver uses — throws
    /// <see cref="System.Text.EncoderFallbackException"/> somewhere far from here, naming a code point the
    /// caller never saw and cannot locate.
    /// </para>
    /// <para>
    /// The announced arithmetic is wrong by the same half character: the marker claims eleven characters were
    /// kept, and eleven code units is ten characters and a fragment.
    /// </para>
    /// </remarks>
    [Fact]
    public void Apply_CutLandingInsideACharacter_DoesNotKeepHalfOfIt()
    {
        // Ten plain characters, then one emoji: the ceiling of eleven falls between its two code units.
        var page = new string('a', 10) + "\U0001F600 and more besides";

        var budgeted = ContentBudget.Of(11).Apply(page);

        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        strict.GetBytes(budgeted.Text);
    }

    /// <summary>A page that writes the truncation marker itself is not truncated on its own words.</summary>
    /// <remarks>
    /// <c>Announced</c> strips a previous budget's marker by searching for its literal text, so any page
    /// containing that text is cut there — before the ceiling is even consulted — and everything after it is
    /// dropped. The marker is a public constant, so the string is not a secret; and a page quoting this
    /// library's own output is the innocent way to arrive here, no attacker required.
    /// <para>
    /// What comes back is a page silently missing its second half, announced as truncated at a length that
    /// has nothing to do with the ceiling.
    /// </para>
    /// </remarks>
    [Fact]
    public void Apply_PageQuotingTheMarker_KeepsWhatFollowsIt()
    {
        var page = $"The first half.\n\n{ContentBudget.Marker}: 5 of 900 characters\nThe second half.";

        var budgeted = ContentBudget.Unlimited.Apply(page);

        Assert.Contains("The second half.", budgeted.Text, StringComparison.Ordinal);
    }

    /// <summary>Markup nested as deep as it may be is read, and the walk that reads it does not recurse.</summary>
    /// <remarks>
    /// This began as the reproduction of the only defect in this file that no retry survives.
    /// <c>TextExtraction.Append</c> walked the document by recursing once per level, and nothing bounded the
    /// depth. MEASURED on 2026-09-14 against HtmlAgilityPack 1.13.0, on a thread with the 1 MB stack the .NET
    /// thread pool gives its workers — which is where <c>Crawler</c> runs every read: six thousand levels
    /// returned, eight thousand raised <c>StackOverflowException</c>, which cannot be caught, runs no
    /// <c>finally</c>, and takes the process with every page in flight. Eight thousand <c>&lt;div&gt;</c> is
    /// forty kilobytes of body.
    /// <para>
    /// The walk is iterative now and this exercises it. WHAT IS WORTH SAYING PLAINLY is that as of the depth
    /// ceiling added on 2026-09-15 this check can no longer reach the depths that broke it: nothing gets past
    /// <c>Markup.MostNesting</c>, and a thousand levels of recursion never threatened a 1 MB stack in the
    /// first place. The iterative walk is now the second line rather than the first, and it stays because it
    /// costs nothing and because it is what makes raising that ceiling a decision about CPU alone. A check
    /// that could tell the two walks apart no longer exists, and pretending otherwise would be worse than
    /// writing this down.
    /// </para>
    /// </remarks>
    [Fact]
    public void Clean_MarkupNestedAsDeepAsItMayBe_ReturnsTheText()
    {
        Assert.Equal("hello", new TextExtraction().Clean(Nested(999)));
    }

    /// <summary>Refusing deep markup costs the same whatever the depth, which time cannot witness.</summary>
    /// <remarks>
    /// A check in milliseconds stood beside this one until 2026-09-15 and was REMOVED, which its own remark
    /// had predicted: a threshold in milliseconds is the flakiest thing a suite can own — it measures the
    /// machine as much as the code, and on a loaded runner it fails for reasons that have nothing to do with
    /// the page. It did exactly that twice in one evening, both times only in a full run of the six suites
    /// beside the browser containers, and the second time it reported 146 ms against its budget of 100.
    /// Nothing was lost by its going: it asserted the ceiling (kept below) and a speed that this check
    /// witnesses better. Allocation does not move with the machine.
    /// <para>
    /// MEASURED on 2026-09-15, per thread:
    /// <list type="bullet">
    /// <item><description>refused — 682 KB at five thousand levels, 686 KB at twenty thousand.</description></item>
    /// <item><description>parsed — 5.3 MB at five thousand, 21.3 MB at twenty thousand.</description></item>
    /// </list>
    /// THE TWO REFUSAL FIGURES BEING THE SAME IS THE POINT, and it is a statement the timing check cannot
    /// make: a parse stopped at a thousand nested nodes does identical work whether the document goes on for
    /// four thousand more levels or nineteen thousand. Cost that stops growing with the input is the whole of
    /// what a ceiling is for.
    /// </para>
    /// <para>
    /// Two megabytes sits three times above the refusal and two and a half times below the cheapest parse,
    /// so it is not a number this can drift past without the ceiling having gone.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(5_000)]
    [InlineData(20_000)]
    public void Clean_MarkupNestedDeeperThanItMayBe_CostsTheSameWhateverTheDepth(int depth)
    {
        var html = Nested(depth);
        var text = new TextExtraction();

        var before = GC.GetAllocatedBytesForCurrentThread();

        var refusal = Assert.Throws<MarkupTooDeepException>(() => text.Clean(html));

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(1_000, refusal.MostNesting);
        Assert.True(
            allocated < 2L * 1024 * 1024,
            $"refusing markup {depth} levels deep cost {allocated / 1024} KB, which is the cost of parsing it");
    }

    /// <summary>The link reader refuses the same markup, because it goes through the same door.</summary>
    /// <remarks>
    /// Worth its own check because the two readers reach the parser separately and only one of them was ever
    /// the one that recursed. HtmlAgilityPack's own XPath walks the tree iteratively — <c>//a[@href]</c> cost
    /// 24 ms at fifty thousand levels — so <c>LinkExtraction</c> never overflowed anything and would happily
    /// have gone on paying seconds of CPU for markup the text reader now turns away. The ceiling is a
    /// property of the parse, not of what is done with it afterwards, which is why it lives in one place both
    /// readers call.
    /// </remarks>
    [Fact]
    public void From_MarkupNestedDeeperThanItMayBe_IsRefusedRatherThanRead()
    {
        var html = Nested(5_000).Replace("hello", "<a href=\"/next\">next</a>", StringComparison.Ordinal);

        Assert.Throws<MarkupTooDeepException>(
            () => new LinkExtraction().From(html, new Uri("https://example.com/")));
    }

    /// <summary>The same reader, on markup shallow enough to be read, still answers.</summary>
    /// <remarks>
    /// The other half of the ceiling, and the half a cure is most likely to get wrong: a guard that refuses
    /// everything passes every check written about refusal. A crawl calls this on every page it reads.
    /// <para>
    /// NINE HUNDRED RATHER THAN THE NINE HUNDRED AND NINETY-NINE the text check uses, and the difference is a
    /// property of the ceiling rather than a fudge. It counts NESTED NODES, not tags of one name, so the
    /// <c>&lt;a&gt;</c> this check needs is itself a level and so is the document node above them all — nine
    /// hundred and ninety-nine <c>&lt;div&gt;</c> with a link inside comes to a thousand and one and is
    /// turned away. Which is correct: what costs the parser is depth, whatever the elements are called.
    /// </para>
    /// </remarks>
    [Fact]
    public void From_MarkupNestedAsDeepAsItMayBe_ReturnsTheLinks()
    {
        var html = Nested(900).Replace("hello", "<a href=\"/next\">next</a>", StringComparison.Ordinal);

        Assert.Single(new LinkExtraction().From(html, new Uri("https://example.com/")));
    }

    private static string Nested(int depth)
        => string.Concat(Enumerable.Repeat("<div>", depth))
           + "hello"
           + string.Concat(Enumerable.Repeat("</div>", depth));
}
