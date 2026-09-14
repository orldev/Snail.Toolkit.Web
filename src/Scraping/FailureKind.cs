namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// The kinds of failure a page read can report.
/// </summary>
/// <remarks>
/// A kind is what a caller branches on: <see cref="Blocked"/> asks for a different identity or proxy,
/// <see cref="Timeout"/> asks for a longer budget, <see cref="RobotsDisallowed"/> asks for nothing at all.
/// The engine this came from answered every one of these with an empty string, so a caller could see that a
/// page was missing but never why.
/// <para>
/// THERE IS DELIBERATELY NO KIND FOR CANCELLATION, and the reason is that every way a read can be cut short
/// already has a name of its own. A caller cancelling propagates as <see cref="OperationCanceledException"/>,
/// which is what the TPL and ASP.NET Core expect and what <c>CrawlerTests</c> holds this library to. A crawl
/// outliving its own budget is <see cref="CrawlStop.TimeLimit"/> in the report, told apart from the caller's
/// cancellation by the one guard that can tell them apart — whose token was cancelled. A navigation outliving
/// <see cref="ScrapingOptions.NavigationTimeoutMs"/> is <see cref="Timeout"/>. A search outliving
/// <c>SearchOptions.Timeout</c> is <c>SearchFailureKind.Canceled</c>. A kind added here would be one nothing
/// could produce, which is worse than the gap it was meant to fill.
/// </para>
/// </remarks>
public enum FailureKind
{
    /// <summary>The site answered with a challenge, a captcha or a refusal page.</summary>
    Blocked,

    /// <summary>The page did not load within the configured navigation budget.</summary>
    /// <remarks>
    /// Cancellation requested by the caller is not reported here: it propagates as
    /// <see cref="OperationCanceledException"/>, which is what the TPL and ASP.NET Core expect.
    /// </remarks>
    Timeout,

    /// <summary>Navigation failed outright — DNS, TLS, a refused connection or an HTTP error status.</summary>
    Navigation,

    /// <summary>No browser could be launched or reached.</summary>
    BrowserUnavailable,

    /// <summary>The configured proxy could not be used.</summary>
    Proxy,

    /// <summary>The site's robots rules forbid this URL for the configured agent.</summary>
    RobotsDisallowed,

    /// <summary>The page loaded and rendered nothing at all.</summary>
    /// <remarks>
    /// Distinct from <see cref="Blocked"/> on purpose: a genuinely empty document is a fact about the site,
    /// while a challenge page is a fact about the client, and the two call for opposite reactions.
    /// </remarks>
    Empty,

    /// <summary>The page loaded, but its markup was refused rather than parsed.</summary>
    /// <remarks>
    /// Today there is one way to earn this and it is nesting depth: parsing is quadratic in it, so markup a
    /// thousand tags deep costs seconds of CPU per page and any site can serve it in a hundred kilobytes.
    /// <para>
    /// A member was added here where <c>Cancelled</c> was refused, and the difference is the whole test for
    /// whether a kind should exist: ANY SITE CAN PRODUCE THIS ONE, with five thousand opening tags and no
    /// closing ones, while nothing in the library could ever have produced a cancellation that was not
    /// already named elsewhere. The alternative was worse than a new member rather than merely different —
    /// refused markup would have come back as <see cref="Empty"/>, which says the site published nothing when
    /// in fact it published a great deal and we declined to read it.
    /// </para>
    /// </remarks>
    Unreadable,

    /// <summary>Something failed that this library does not classify further.</summary>
    Unexpected
}
