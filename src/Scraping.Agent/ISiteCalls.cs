namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// Asks a site's own service the way its page would, and hands back what it answered.
/// </summary>
/// <remarks>
/// Separate from <see cref="IPageAgent"/> because the answer is a different thing. A walkthrough ends in a
/// rendered page and is reported as one; this ends in whatever the service returned — usually JSON — and
/// putting that into a page's text would be a forgery, dressing a payload up as something a reader saw.
/// </remarks>
public interface ISiteCalls
{
    /// <summary>Opens the page, mints what the service demands, sends the request and returns its body.</summary>
    Task<Outcome<string, PageFailure>> SendAsync(SiteCall call, CancellationToken ct = default);
}
