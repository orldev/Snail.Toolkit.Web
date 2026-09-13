using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the limit neither engine this was assembled from had.
/// </summary>
public class ContentBudgetTests
{
    [Fact]
    public void Apply_TextWithinTheBudget_IsHandedBackUntouched()
    {
        var result = ContentBudget.Of(100).Apply("short");

        Assert.Equal("short", result.Text);
        Assert.False(result.IsTruncated);
        Assert.Equal(5, result.OriginalLength);
    }

    /// <summary>
    /// A model handed half a document with no sign of the cut answers confidently about a page it has not
    /// seen, so the cut is announced inside the text as well as beside it.
    /// </summary>
    [Fact]
    public void Apply_TextOverTheBudget_SaysSoInTheTextItself()
    {
        var result = ContentBudget.Of(10).Apply(new string('a', 50));

        Assert.True(result.IsTruncated);
        Assert.Contains(ContentBudget.Marker, result.Text, StringComparison.Ordinal);
        Assert.StartsWith(new string('a', 10), result.Text, StringComparison.Ordinal);
    }

    /// <summary>Both lengths are named, so a caller can decide whether the rest is worth another fetch.</summary>
    [Fact]
    public void Apply_TextOverTheBudget_NamesWhatWasKeptAndWhatThereWas()
    {
        var result = ContentBudget.Of(10).Apply(new string('a', 50));

        Assert.Contains("10 of 50 characters", result.Text, StringComparison.Ordinal);
        Assert.Equal(50, result.OriginalLength);
    }

    [Fact]
    public void Apply_UnlimitedBudget_NeverTruncates()
    {
        var result = ContentBudget.Unlimited.Apply(new string('a', 1_000_000));

        Assert.False(result.IsTruncated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Of_BudgetThatCouldKeepNothing_IsRefused(int maxCharacters)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ContentBudget.Of(maxCharacters));

    /// <summary>A second budget describes the page, never the first budget's answer.</summary>
    /// <remarks>
    /// A crawl budgets what the reader already budgeted. Measured on 2026-09-13, nine hundred characters came
    /// back announced as "60 of 164": the second cut named the length of the first cut's output, and a model
    /// reading that believes it saw a third of a short page rather than a fifteenth of a long one.
    /// </remarks>
    [Fact]
    public void Apply_TextAnotherBudgetAlreadyCut_NamesThePageLengthRatherThanTheEarlierCut()
    {
        var page = new string('a', 900);
        var read = ContentBudget.Of(120).Apply(page);

        var crawled = ContentBudget.Of(60).Apply(read.Text, read.OriginalLength);

        Assert.Contains("60 of 900 characters", crawled.Text, StringComparison.Ordinal);
        Assert.Equal(900, crawled.OriginalLength);
        Assert.True(crawled.IsTruncated);
    }

    /// <summary>One cut, one announcement — however many budgets the text passed through.</summary>
    /// <remarks>
    /// The earlier announcement is stripped before the cut rather than after. Cutting first left the ceiling
    /// landing in the middle of it, so the text ended on half a sentence claiming a length.
    /// </remarks>
    [Theory]
    [InlineData(60)]
    [InlineData(150)]
    [InlineData(1000)]
    public void Apply_TextAnotherBudgetAlreadyCut_CarriesOneMarker(int second)
    {
        var read = ContentBudget.Of(120).Apply(new string('a', 900));

        var crawled = ContentBudget.Of(second).Apply(read.Text, read.OriginalLength);

        Assert.Equal(1, Markers(crawled.Text));
        Assert.EndsWith("of 900 characters", crawled.Text, StringComparison.Ordinal);
    }

    /// <summary>A budget wider than the text still says the page was cut, because an earlier one cut it.</summary>
    [Fact]
    public void Apply_TextAnotherBudgetCutButThisOneWouldNot_StillSaysSo()
    {
        var read = ContentBudget.Of(120).Apply(new string('a', 900));

        var crawled = ContentBudget.Unlimited.Apply(read.Text, read.OriginalLength);

        Assert.True(crawled.IsTruncated);
        Assert.Contains("120 of 900 characters", crawled.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_OriginalLengthBelowZero_IsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ContentBudget.Of(10).Apply("text", -1));

    private static int Markers(string text)
        => text.Split(ContentBudget.Marker, StringSplitOptions.None).Length - 1;
}
