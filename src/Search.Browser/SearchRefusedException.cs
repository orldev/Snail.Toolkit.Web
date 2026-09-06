using Snail.Toolkit.Web.Scraping;

namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// Thrown when a results page could not be read at all.
/// </summary>
/// <remarks>
/// Reading a page answers with failure as data, while a search provider is contracted to throw: a provider that
/// answered an empty list after a consent wall would be indistinguishable from one that simply found nothing,
/// and that distinction is the whole reason the outcome type exists. This is where the two conventions meet, so
/// it carries the original <see cref="PageFailure"/> rather than flattening it into a message — and names the
/// engine, because a caller running several browser engines needs to know which wall it hit.
/// </remarks>
public sealed class SearchRefusedException(string provider, PageFailure failure)
    : Exception($"{provider} could not be read. {failure}")
{
    /// <summary>Gets the engine whose page could not be read.</summary>
    public string Provider { get; } = provider;

    /// <summary>Gets why the page could not be read.</summary>
    public PageFailure Failure { get; } = failure;
}
