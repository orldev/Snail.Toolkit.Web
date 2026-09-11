namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// A request to a site's own service, made from inside one of its pages.
/// </summary>
/// <remarks>
/// <see cref="Path"/> is relative on purpose: the request goes out from the page, so it carries that page's
/// origin, cookies and whatever clearance it was granted on the way in. That is the whole point — the service
/// answers a visitor, not a stranger.
/// </remarks>
public sealed record SiteCall(Link Page, string Path, string Body)
{
    /// <summary>The credential the page has to issue first, when the service demands one.</summary>
    public Mint? Mint { get; init; }

    /// <summary>The HTTP method the service expects.</summary>
    public string Method { get; init; } = "POST";

    /// <summary>The content type of <see cref="Body"/>.</summary>
    public string ContentType { get; init; } = "application/json";

    /// <summary>How long the page is left to finish loading its own scripts before it is asked to mint.</summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(3);
}
