using Snail.Toolkit.Web.Scraping;

namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// Thrown when the engine met the results page with a challenge, a refusal page or a request to come back later.
/// </summary>
/// <remarks>
/// The browser-side shape of <see cref="SearchRefusedException"/>: the search catches the base and rests the
/// engine, while this keeps the original <see cref="PageFailure"/> for a caller who wants to know whether it was
/// a challenge or a 429, and under which identity.
/// </remarks>
public sealed class PageRefusedException(string provider, PageFailure failure)
    : SearchRefusedException(provider, failure.ToString())
{
    /// <summary>Gets what the engine answered with instead of a results page.</summary>
    public PageFailure Failure { get; } = failure;
}
