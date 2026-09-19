using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Search;

/// <summary>
/// Why one attempt carried no results.
/// </summary>
public enum SearchFailureKind
{
    /// <summary>The provider answered and the answer held nothing.</summary>
    /// <remarks>
    /// This is an answer, not a failure of the provider — which is why
    /// <see cref="SearchOutcome.IsAnswered"/> counts it as one.
    /// </remarks>
    NoResults,

    /// <summary>The provider threw.</summary>
    Faulted,

    /// <summary>The caller cancelled before the provider answered.</summary>
    Canceled,

    /// <summary>The engine answered, and the answer is markup this library will not parse.</summary>
    /// <remarks>
    /// Kept apart from <see cref="Faulted"/>, which it would otherwise be folded into, because the two send a
    /// reader to different places. A fault is a defect on this side and the answer is to look at it; this is a
    /// page refused on purpose by a ceiling on nesting depth, and there is nothing to fix — the measurement
    /// behind that ceiling is recorded on the parser. Kept apart from <see cref="NoResults"/> for the sharper
    /// reason: that one is a statement about the QUERY, and reporting a page nobody read as "nothing matched"
    /// is the single answer here that would be untrue.
    /// <para>
    /// It rests no engine. A refusal is the engine asking to be left alone; markup too deep to read says
    /// nothing about how often it is willing to be asked.
    /// </para>
    /// </remarks>
    Unreadable,

    /// <summary>The provider was not asked at all: it is resting after a refusal.</summary>
    /// <remarks>
    /// Not a failure of the engine and not an answer from it — it is this library declining to ask, and until
    /// 2026-09-15 it was visible only in the log. The caller needs it because the two sentences it tells apart
    /// call for opposite reactions: "every engine answered and nothing matched" is a fact about the query,
    /// while "the engines that know this kind of question are all resting" is a fact about the search, and
    /// both arrived as an outcome with no results in it.
    /// <para>
    /// The instant the rest ends travels with it in <see cref="SearchFailure.OpensAt"/>, because a caller
    /// writing that sentence for a person has to name a time rather than reproduce prose.
    /// </para>
    /// </remarks>
    Resting,

    /// <summary>The provider was not asked: it could not answer inside the budget this search was given.</summary>
    /// <remarks>
    /// Apart from <see cref="Canceled"/>, which it used to arrive as, and the difference is who decided: a
    /// caller that cancels has changed its mind, while this is the search keeping the promise the caller made
    /// it. Folded together, a deployment could not tell a deadline it had set from a user closing a page.
    /// <para>
    /// Two shapes of the same fact. Either the host this engine talks to does not open inside the budget —
    /// known BEFORE asking, from the per-host gate, and then <see cref="SearchFailure.OpensAt"/> names when it
    /// would have — or the budget ran out while other providers were answering.
    /// </para>
    /// </remarks>
    OutOfTime,

    /// <summary>The provider was turned away: a challenge, a consent wall, a rate limit.</summary>
    /// <remarks>
    /// Kept apart from <see cref="Faulted"/> because the two call for opposite reactions. A fault is a defect
    /// on this side — a parser that no longer follows the markup, a socket that died — and the right answer is
    /// to look at it. A refusal is the engine asking to be left alone, and the right answer is to leave it
    /// alone for a while, which is what <see cref="ISearchCooldown"/> does with it. Folding the two together,
    /// as the search once did, would have rested an engine over our own bug.
    /// </remarks>
    Refused
}

/// <summary>
/// The reason an attempt produced no results.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="SearchFailureKind"/> alone because a fault is only actionable with the message
/// the provider threw, while <see cref="SearchFailureKind.NoResults"/> has nothing to add.
/// </remarks>
public sealed record SearchFailure(SearchFailureKind Kind, string? Message = null)
{
    /// <summary>Gets the soonest this provider could be asked, when that is known.</summary>
    /// <remarks>
    /// Carried as an instant rather than only in the message, for the same reason
    /// <c>PageFailure.RetryAfter</c> is: a caller deciding whether to wait or to report has to compare it with
    /// a clock. One field rather than one per kind, and the wording is the gate's own
    /// (<c>IPoliteness.OpensAt</c>), because the two kinds that set it are the same fact with different
    /// causes: <see cref="SearchFailureKind.Resting"/> is the engine holding itself back after a refusal,
    /// <see cref="SearchFailureKind.OutOfTime"/> the per-host gate holding it back — and a caller writing
    /// "not asked, ask again after X" does not care which.
    /// </remarks>
    public DateTimeOffset? OpensAt { get; init; }

    /// <summary>The reason a provider was left out of a search rather than asked.</summary>
    public static SearchFailure Resting(string provider, DateTimeOffset until)
        => new(SearchFailureKind.Resting, $"{provider} was not asked: resting after a refusal until {until:u}.")
        {
            OpensAt = until
        };

    /// <summary>The reason a provider whose host opens too late was left out of a search.</summary>
    public static SearchFailure TooLate(string provider, DateTimeOffset opens)
        => new(
            SearchFailureKind.OutOfTime,
            $"{provider} was not asked: the host it talks to opens at {opens:u}, past the budget for this search.")
        {
            OpensAt = opens
        };

    /// <summary>The reason a provider whose host stays shut for longer than one request may take was left out.</summary>
    /// <remarks>
    /// The same fact as <see cref="TooLate"/> measured against a different promise: there the caller named a
    /// budget, here the deployment named how long one request may take. Both are the gate answering before the
    /// request rather than the request waiting at it.
    /// </remarks>
    public static SearchFailure Shut(string provider, DateTimeOffset opens, TimeSpan limit)
        => new(
            SearchFailureKind.OutOfTime,
            $"{provider} was not asked: the host it talks to opens at {opens:u}, later than the {limit} one request may take.")
        {
            OpensAt = opens
        };

    /// <summary>The reason a provider nobody reached before the budget ran out was left out of a search.</summary>
    /// <remarks>
    /// No instant here on purpose: the budget ran out, and when this provider's host would next open is a
    /// question nobody asked — answering it would mean consulting the gate for an engine the search has
    /// already decided against, and an invented instant reads exactly like a measured one.
    /// </remarks>
    public static SearchFailure OutOfTime(string provider)
        => new(SearchFailureKind.OutOfTime, $"{provider} was not asked: the budget for this search ran out.");

    /// <summary>The reason a page of results about something else is recorded as a refusal.</summary>
    /// <remarks>
    /// A refusal rather than an empty answer, because that is what it is: the engine did not answer THIS
    /// caller, and the caller that hears "nothing found" would go on to say so about the world. It rests the
    /// engine, which is the right reaction to a wall served as a page of links.
    /// </remarks>
    public static SearchFailure Unrelated(string provider, int results)
        => new(
            SearchFailureKind.Refused,
            $"{provider} answered a page of {results} results about something other than the query.");

    /// <summary>The reason a page was refused by the ceiling on nesting depth rather than read.</summary>
    /// <remarks>
    /// Worded once because four engines say it: the message names the engine, since a caller running several
    /// of them has to know whose page it was, and names the ceiling rather than the depth the page reached —
    /// the parser stops counting at the ceiling and never learns how much deeper the markup went.
    /// </remarks>
    public static SearchFailure Unreadable(string provider, MarkupTooDeepException cause)
    {
        ArgumentNullException.ThrowIfNull(cause);

        return new SearchFailure(
            SearchFailureKind.Unreadable,
            $"{provider} answered markup nested deeper than the {cause.MostNesting} tags this library parses.");
    }
}
