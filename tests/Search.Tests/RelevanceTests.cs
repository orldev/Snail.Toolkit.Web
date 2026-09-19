namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// Covers the one glance that tells an answer from a page of confident links about something else.
/// </summary>
/// <remarks>
/// The numbers come from the measurement of 2026-09-15: a real answer mentioned the query in eight to ten
/// results out of ten, a stub in none of them. The tests below sit on both sides of the gap and on the ceiling
/// itself, so that moving the ceiling is a decision somebody makes rather than a line that quietly drifts.
/// </remarks>
public class RelevanceTests
{
    [Fact]
    public void Answers_PageWhereNothingMentionsTheQuery_IsNotAnAnswer()
        => Assert.False(Relevance.Answers("отзыв лицензии КИВИ Банк", Results(10, mentioning: 0)));

    /// <summary>
    /// One accidental match in ten is what the ceiling is for: the engine that had this check asked for a
    /// single result, and a stub carrying a hotel chain whose page happens to hold the word passed as an answer.
    /// </summary>
    [Fact]
    public void Answers_PageWhereOneResultOfTenMentionsTheQuery_IsNotAnAnswer()
        => Assert.False(Relevance.Answers("отзыв лицензии КИВИ Банк", Results(10, mentioning: 1)));

    [Fact]
    public void Answers_PageWhereMostResultsMentionTheQuery_IsAnAnswer()
        => Assert.True(Relevance.Answers("отзыв лицензии КИВИ Банк", Results(10, mentioning: 8)));

    /// <summary>The ceiling itself, named as a number of results so that the arithmetic is visible.</summary>
    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void Answers_PageAtTheCeiling_IsDecidedByTheShare(int mentioning, bool expected)
        => Assert.Equal(expected, Relevance.Answers("роснано иск суд", Results(10, mentioning)));

    /// <summary>A match in the snippet counts: the title is not the only place an engine says what it found.</summary>
    [Fact]
    public void Answers_QueryFoundOnlyInTheSnippets_IsAnAnswer()
    {
        IReadOnlyList<SearchResult> results =
        [
            .. Enumerable.Range(0, 3).Select(index => new SearchResult
            {
                Title = $"Untitled {index}",
                Address = Link.Of($"https://example.com/{index}"),
                Snippet = "Подробности об отзыве лицензии",
                Provider = "Probe"
            })
        ];

        Assert.True(Relevance.Answers("отзыв лицензии", results));
    }

    /// <summary>
    /// A query of short words is believed: what a three-letter word matches is the engine's own chrome, and
    /// resting an engine on that evidence is worse than taking its answer.
    /// </summary>
    [Fact]
    public void Answers_QueryWithNoWordLongEnoughToLookFor_IsBelieved()
        => Assert.True(Relevance.Answers("C# 14", Results(10, mentioning: 0)));

    /// <summary>An engine that found nothing said so, and that is an answer about the query, not about us.</summary>
    [Fact]
    public void Answers_PageWithNoResultsAtAll_IsBelieved()
        => Assert.True(Relevance.Answers("роснано иск суд", []));

    /// <summary>
    /// Below the sample the share is not read: one mention of four is a quarter, and a real answer produces
    /// that whenever its titles paraphrase. The page this rule was measured against carried ten results.
    /// </summary>
    [Fact]
    public void Answers_FewResultsOneOfWhichMentionsTheQuery_IsAnAnswer()
        => Assert.True(Relevance.Answers("роснано иск суд", Results(4, mentioning: 1)));

    /// <summary>And below the sample a page mentioning nothing is still not an answer.</summary>
    [Fact]
    public void Answers_FewResultsNoneOfWhichMentionTheQuery_IsNotAnAnswer()
        => Assert.False(Relevance.Answers("роснано иск суд", Results(4, mentioning: 0)));

    private static IReadOnlyList<SearchResult> Results(int count, int mentioning)
        =>
        [
            .. Enumerable.Range(0, count).Select(index => new SearchResult
            {
                Title = index < mentioning ? $"Отозвана лицензии у банка, часть {index}" : $"Hotel booking {index}",
                Address = Link.Of($"https://example.com/{index}"),
                Snippet = index < mentioning ? "О суде и роснано" : "Rooms and rates",
                Provider = "Probe"
            })
        ];
}
