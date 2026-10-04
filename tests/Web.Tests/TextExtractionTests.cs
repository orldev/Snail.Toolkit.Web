using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers text extraction, including the document shape that used to throw, and the page's own name.
/// </summary>
public class TextExtractionTests
{
    private readonly TextExtraction _extraction = new();

    /// <summary>
    /// Regression: <c>SelectNodes</c> answers null when nothing matches, and the result was dereferenced
    /// unconditionally, so any page without a script, style, noscript or frame threw.
    /// </summary>
    [Fact]
    public void Clean_DocumentWithoutScriptsOrStyles_ReturnsText()
    {
        var text = _extraction.Clean("<html><body><p>hi</p></body></html>");

        Assert.Equal("hi", text);
    }

    [Fact]
    public void Clean_ScriptAndStyle_AreRemoved()
    {
        var text = _extraction.Clean(
            "<html><head><style>p{color:red}</style></head><body><script>alert(1)</script><p>visible</p></body></html>");

        Assert.Equal("visible", text);
    }

    /// <summary>Paragraph boundaries survive, because a model cannot tell a heading from a sentence without them.</summary>
    [Fact]
    public void Clean_SeparateBlocks_KeepLineBreaks()
    {
        var text = _extraction.Clean("<body><h1>Title</h1><p>First</p><p>Second</p></body>");

        Assert.Equal(["Title", "First", "Second"], text.Split('\n'));
    }

    [Fact]
    public void Clean_HtmlEntities_AreDecoded()
        => Assert.Equal("a & b", _extraction.Clean("<p>a &amp; b</p>"));

    [Fact]
    public void Clean_RepeatedWhitespace_IsCollapsed()
        => Assert.Equal("a b", _extraction.Clean("<p>a     \t  b</p>"));

    /// <summary>A line break in the source is whitespace, not a paragraph: only a block ends a line.</summary>
    /// <remarks>
    /// Measured on 2026-10-04 through the MCP server built on this toolkit, on a page of the PostgreSQL
    /// manual: «please use / this form / to report a documentation issue» came back as three lines, because
    /// the sentence is written across three source lines with a link in the middle. Every inline element on
    /// a line of its own became a line of the text, and a navigation bar became a column of single words.
    /// </remarks>
    [Fact]
    public void Clean_SentenceWrittenAcrossSourceLines_StaysOneLine()
    {
        var text = _extraction.Clean(
            """
            <p>If you see anything that is not correct, please use
              <a href="/account/comments/new/">this form</a>
              to report a documentation issue.</p>
            """);

        Assert.Equal("If you see anything that is not correct, please use this form to report a documentation issue.", text);
    }

    /// <summary>The name of a page is returned as its title and is not the first words of its text.</summary>
    /// <remarks>
    /// Measured the same day on example.com: the text began «Example DomainThis domain is for use in
    /// documentation examples» — the title element, which is not a block, run straight into the first
    /// paragraph of the body.
    /// </remarks>
    [Fact]
    public void Clean_DocumentWithATitle_DoesNotRepeatItInTheText()
    {
        var text = _extraction.Clean(
            "<html><head><title>Example Domain</title></head><body><div><h1>Example Domain</h1><p>This domain is for use in examples.</p></div></body></html>");

        Assert.Equal(["Example Domain", "This domain is for use in examples."], text.Split('\n'));
    }

    /// <summary>Text standing directly before a block is a line of its own, not the start of that block.</summary>
    [Fact]
    public void Clean_TextFollowedByABlock_IsSeparatedFromIt()
    {
        var text = _extraction.Clean("<div>Introduction<p>First paragraph</p>and a closing remark</div>");

        Assert.Equal(["Introduction", "First paragraph", "and a closing remark"], text.Split('\n'));
    }

    /// <summary>Preformatted text is the one place where a line break in the source is the author's.</summary>
    [Fact]
    public void Clean_PreformattedText_KeepsItsLines()
    {
        var text = _extraction.Clean("<pre>SELECT 1;\nSELECT 2;</pre>");

        Assert.Equal(["SELECT 1;", "SELECT 2;"], text.Split('\n'));
    }

    /// <summary>The cells of a row are words apart, and the rows are lines apart.</summary>
    [Fact]
    public void Clean_Table_KeepsRowsOnLinesAndCellsApart()
    {
        var text = _extraction.Clean("<table><tr><th>Name</th><th>Age</th></tr><tr><td>Ann</td><td>30</td></tr></table>");

        Assert.Equal(["Name Age", "Ann 30"], text.Split('\n'));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Clean_BlankInput_ReturnsEmpty(string html)
        => Assert.Equal(string.Empty, _extraction.Clean(html));

    /// <summary>The name a page gives itself comes back the way a tab would show it: decoded, on one line.</summary>
    [Fact]
    public void Title_DocumentWithATitle_ReturnsItDecodedAndOnOneLine()
        => Assert.Equal(
            "Grain & barley — prices",
            _extraction.Title("<html><head><title>  Grain &amp; barley\n   — prices </title></head><body><h1>Other</h1></body></html>"));

    /// <summary>A page that names itself only in its heading is still named.</summary>
    [Fact]
    public void Title_DocumentWithoutATitleButWithAHeading_ReturnsTheHeading()
        => Assert.Equal("Only heading", _extraction.Title("<body><h1>Only heading</h1><p>text</p></body>"));

    [Fact]
    public void Title_DocumentWithNeither_ReturnsNull()
        => Assert.Null(_extraction.Title("<body><p>just text</p></body>"));

    /// <summary>An inline drawing's own title names the drawing, not the page.</summary>
    [Fact]
    public void Title_TitleInsideAnInlineDrawing_IsNotThePages()
        => Assert.Equal("Heading", _extraction.Title("<body><svg><title>icon</title></svg><h1>Heading</h1></body>"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><head><title>   </title></head></html>")]
    public void Title_BlankInput_ReturnsNull(string html)
        => Assert.Null(_extraction.Title(html));
}
