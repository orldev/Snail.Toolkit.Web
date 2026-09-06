namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the shape both engines answer with, where failure is data rather than an empty string.
/// </summary>
public class OutcomeTests
{
    [Fact]
    public void Success_CarriesTheValueAndNoFailure()
    {
        var outcome = Outcome<string, string>.Success("page");

        Assert.True(outcome.IsSuccess);
        Assert.Equal("page", outcome.Value);
        Assert.Null(outcome.Failure);
    }

    [Fact]
    public void Failed_CarriesTheFailureAndNoValue()
    {
        var outcome = Outcome<string, string>.Failed("blocked");

        Assert.False(outcome.IsSuccess);
        Assert.Equal("blocked", outcome.Failure);
    }

    /// <summary>Reading the value of a failure is a mistake in the caller, not a runtime condition.</summary>
    [Fact]
    public void Value_OnAFailedOutcome_Throws()
    {
        var outcome = Outcome<string, string>.Failed("blocked");

        Assert.Throws<InvalidOperationException>(() => outcome.Value);
    }

    [Fact]
    public void TryGetValue_OnASuccess_HandsOutTheValue()
    {
        var outcome = Outcome<string, string>.Success("page");

        Assert.True(outcome.TryGetValue(out var value));
        Assert.Equal("page", value);
    }

    [Fact]
    public void TryGetValue_OnAFailure_HandsOutNothing()
    {
        var outcome = Outcome<string, string>.Failed("blocked");

        Assert.False(outcome.TryGetValue(out var value));
        Assert.Null(value);
    }

    /// <summary>Success and "there is something to read" have to stay the same fact.</summary>
    [Fact]
    public void Success_WithoutAValue_IsRefused()
        => Assert.Throws<ArgumentNullException>(() => Outcome<string, string>.Success(null!));

    [Fact]
    public void Failed_WithoutAFailure_IsRefused()
        => Assert.Throws<ArgumentNullException>(() => Outcome<string, string>.Failed(null!));
}
