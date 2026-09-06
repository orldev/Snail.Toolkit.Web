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
}
