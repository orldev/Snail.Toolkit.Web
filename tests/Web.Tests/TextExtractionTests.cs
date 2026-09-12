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
