namespace Snail.Toolkit.Web.Search;

/// <summary>
/// Tells an answer to the query from a page of confident results about something else.
/// </summary>
/// <remarks>
/// The worst of the three shapes an engine can answer in. An empty page is read as "nothing found" and a
/// refusal is read as a refusal, but a page of ten unrelated links is structurally an answer: measured on
/// 2026-09-15 from a datacentre address, one engine answered «"Роснано"» with biblical sites and Netflix,
/// «Роснано» without the quotes with a chain of hotels, and «отзыв лицензии КИВИ Банк» with car parts and
/// Microsoft sign-in pages. A parser cannot tell that from an answer; a reader can, at a glance, and this is
/// that glance written down.
/// <para>
/// THE RULE AND THE NUMBERS BEHIND IT. What is counted is the share of results whose title or snippet holds at
/// least one word of the query of four letters or more. The same measurement put a real answer at eight to ten
/// out of ten and a stub at zero out of ten — two clusters with nothing in between, and the ceiling here is a
/// third, which is in that gap and closer to the stub. Until 2026-09-20 the engine that had this check asked
/// for ONE result out of however many, so a stub carrying a single accidental match passed as an answer.
/// </para>
/// <para>
/// WHAT A WRONG ANSWER COSTS, both ways. Reading a stub as an answer hands the caller ten confident links
/// about nothing, and the caller has no way to tell. Reading an answer as a stub records a refusal, which
/// rests that engine for the configured cooldown — the other engines answer the query meanwhile and the rest
/// ends by itself. The second is the cheaper mistake, which is why the ceiling sits low rather than in the
/// middle.
/// </para>
/// <para>
/// Words shorter than four letters are left out because they match everything: an engine's own chrome, a
/// navigation link, the word "the". Matching is by substring and case-insensitive, so a title inflecting a
/// word the query gave in another form is a miss rather than a match — the rule is deliberately crude, since
/// what it separates is a cluster at eight from a cluster at zero.
/// </para>
/// </remarks>
internal static class Relevance
{
    /// <summary>The share of results that must mention the query for the page to be an answer to it.</summary>
    private const double Least = 1.0 / 3.0;

    /// <summary>The shortest query word worth looking for in a result.</summary>
    private const int ShortestWord = 4;

    /// <summary>The fewest results on which a share is worth reading as one.</summary>
    /// <remarks>
    /// A share is a sample, and on four results one mention is a quarter — inside the noise of a real answer,
    /// where a title may paraphrase what it found or name it in another form. The page this rule was measured
    /// against carried ten. Below the sample a page is still refused when NOTHING on it mentions the query,
    /// which is the measurement's own finding; what is not done below it is arithmetic on three numbers.
    /// </remarks>
    private const int Sample = 5;

    private static readonly char[] Breaks = [' ', '"', '\'', '+', ',', ';', ':', '(', ')'];

    /// <summary>Whether these results are about that query, as far as the words can say.</summary>
    /// <remarks>
    /// A query with no word long enough to look for is answered yes: the evidence would be the engine's
    /// chrome rather than the query, and refusing an engine on that is worse than believing it.
    /// </remarks>
    public static bool Answers(string query, IReadOnlyList<SearchResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var words = Words(query);

        if (words.Count == 0 || results.Count == 0)
            return true;

        var mentioning = results.Count(result => words.Any(word =>
            result.Title.Contains(word, StringComparison.OrdinalIgnoreCase)
            || result.Snippet.Contains(word, StringComparison.OrdinalIgnoreCase)));

        return results.Count < Sample ? mentioning > 0 : mentioning >= results.Count * Least;
    }

    private static IReadOnlyList<string> Words(string query)
        =>
        [
            .. (query ?? string.Empty)
                .Split(Breaks, StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length >= ShortestWord)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
}
