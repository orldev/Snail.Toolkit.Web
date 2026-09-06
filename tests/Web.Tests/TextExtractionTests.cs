using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers text extraction, including the document shape that used to throw.
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
}
