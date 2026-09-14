using Snail.Toolkit.Web.Search;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// What every engine's live answer has to look like, whatever engine it came from.
/// </summary>
/// <remarks>
/// A live request is the scarce thing here, not a test method: an engine that answers frequency with a
/// challenge cannot be asked four times to make four assertions. So each engine is asked once and the answer
/// is read by several named checks, and these are the checks they share.
/// <para>
/// They exist because a parser fails quietly. Every engine here is anchored on markup nobody in this
/// repository controls — class names carrying a build hash at Brave, <c>b_algo</c> at Bing,
/// <c>result__a</c> at DuckDuckGo — and when one of those stops matching, the provider answers success with
/// nothing in it, which reads exactly like a query that found nothing.
/// </para>
/// </remarks>
internal static class LiveAnswer
{
    /// <summary>Reads the results out of an answer, failing with the engine's own words when there are none.</summary>
    public static IReadOnlyList<SearchResult> Of(SearchAnswer answered)
    {
        Assert.True(answered.IsSuccess, answered.Failure?.ToString());
        Assert.NotEmpty(answered.Value);

        return answered.Value;
    }

    /// <summary>Every address leads somewhere a caller can go, and nowhere on the engine itself.</summary>
    /// <remarks>
    /// One assertion for two failures that look nothing alike from here. An engine that starts wrapping its
    /// links hands back something with no host at all — which is what Google began doing with <c>/goto</c>,
    /// and what cost a night before it was understood. An engine whose furniture stops being recognised hands
    /// back its own sign-in and help pages as answers.
    /// </remarks>
    public static void LeadsAway(IReadOnlyList<SearchResult> results, string engineHost)
    {
        Assert.All(results, result => Assert.True(result.Address.IsAbsolute, result.Address.Text));

        Assert.DoesNotContain(
            results,
            result => result.Address.Address!.Host.EndsWith(engineHost, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>At least one result carries a heading.</summary>
    /// <remarks>
    /// "At least one" rather than "every one", and that is the whole point of the check. A result without a
    /// heading is ordinary — image, video and knowledge blocks carry a link and no prose — so a parser whose
    /// heading selector stopped matching altogether produces a page of ordinary-looking results and no
    /// failure anywhere. None at all is the layout change; one missing is a Tuesday.
    /// </remarks>
    public static void Titled(IReadOnlyList<SearchResult> results)
        => Assert.Contains(results, result => result.Title.Length > 0);

    /// <summary>At least one result carries the engine's own description of the page.</summary>
    /// <remarks>Same reasoning as <see cref="Titled"/>: a missing snippet is ordinary, all of them missing is not.</remarks>
    public static void Described(IReadOnlyList<SearchResult> results)
        => Assert.Contains(results, result => result.Snippet.Length > 0);

    /// <summary>The answer names the site the query was about.</summary>
    /// <remarks>
    /// The one assertion that reads the world rather than the shape. A count would pass on a page built
    /// entirely out of the engine's own links; this fails unless the engine actually answered the question.
    /// </remarks>
    public static void About(IReadOnlyList<SearchResult> results, string host)
        => Assert.Contains(
            results,
            result => result.Address.Address!.Host.EndsWith(host, StringComparison.OrdinalIgnoreCase));
}
