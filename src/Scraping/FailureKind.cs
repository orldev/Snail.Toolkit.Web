namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// The kinds of failure a page read can report.
/// </summary>
/// <remarks>
/// A kind is what a caller branches on: <see cref="Blocked"/> asks for a different identity or proxy,
/// <see cref="Timeout"/> asks for a longer budget, <see cref="RobotsDisallowed"/> asks for nothing at all.
/// The engine this came from answered every one of these with an empty string, so a caller could see that a
/// page was missing but never why.
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

    /// <summary>Something failed that this library does not classify further.</summary>
    Unexpected
}
