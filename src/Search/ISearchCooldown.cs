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
    /// <summary>Answers whether a provider is resting and must not be asked.</summary>
    bool IsResting(string provider);

    /// <summary>Records that a provider refused, and answers how long it will now be left alone.</summary>
    TimeSpan Refused(string provider);

    /// <summary>Records that a provider answered, which ends its rest and forgets its earlier refusals.</summary>
    /// <remarks>An empty answer counts: the engine let the request through, and that is the whole question here.</remarks>
    void Answered(string provider);
}
