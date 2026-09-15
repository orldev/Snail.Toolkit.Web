namespace Snail.Toolkit.Web.Search;

/// <summary>
/// Remembers which providers refused recently and keeps them out of the next searches for a while.
/// </summary>
/// <remarks>
/// Politeness towards the engine, as distinct from politeness towards a host: the per-host gate spaces the
/// requests that are made, and this decides whether one is made at all. A provider that has just answered with
/// a challenge will answer the next request the same way if it comes at once, and every such request makes the
/// eventual ban more likely — so the search stops asking rather than asking more carefully.
/// </remarks>
public interface ISearchCooldown
{
    /// <summary>When the rest of a provider that must not be asked ends, or nothing when it may be asked.</summary>
    /// <remarks>
    /// The instant rather than a yes-or-no since 2026-09-15, and for a reason outside this interface: a
    /// provider left out of a search is a fact the CALLER has to be able to state — "not asked, resting until
    /// 14:20" reads differently from "asked and said nothing", and a boolean can only produce the first half
    /// of that sentence. Answering nothing rather than an instant in the past keeps the question single: a
    /// caller asks whether it may ask, and gets the reason it may not.
    /// </remarks>
    DateTimeOffset? RestingUntil(string provider);

    /// <summary>Records that a provider refused, and answers how long it will now be left alone.</summary>
    TimeSpan Refused(string provider);

    /// <summary>Records that a provider answered, which ends its rest and forgets its earlier refusals.</summary>
    /// <remarks>An empty answer counts: the engine let the request through, and that is the whole question here.</remarks>
    void Answered(string provider);
}
