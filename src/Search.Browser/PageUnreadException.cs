using Snail.Toolkit.Web.Scraping;

namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// Thrown when a results page could not be read, for a reason that says nothing about the engine's patience.
/// </summary>
/// <remarks>
/// Reading a page answers with failure as data, while a search provider is contracted to throw: a provider that
/// answered an empty list after a timeout would be indistinguishable from one that simply found nothing, and
/// that distinction is the whole reason the outcome type exists. This is where the two conventions meet, so it
/// carries the original <see cref="PageFailure"/> rather than flattening it into a message — and names the
/// engine, because a caller running several browser engines needs to know which one it was.
/// <para>
/// A page the engine refused to show is <see cref="PageRefusedException"/> instead. The search rests an engine
/// over that and must not over this: a navigation timeout, an unreachable browser or a proxy that would not
/// connect is trouble on this side, and resting the engine would only hide it for as long as the rest lasts.
/// </para>
/// </remarks>
public sealed class PageUnreadException(string provider, PageFailure failure)
    : Exception($"{provider} could not be read. {failure}")
{
    /// <summary>Gets the engine whose page could not be read.</summary>
    public string Provider { get; } = provider;

    /// <summary>Gets why the page could not be read.</summary>
    public PageFailure Failure { get; } = failure;
}
