namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Keeps what a site left with us, so the next visit to it is not a first visit.
/// </summary>
/// <remarks>
/// A cleared challenge is a cookie: Cloudflare answers with <c>cf_clearance</c>, a consent wall with a
/// preference, a registry with a session. All of them are the site saying «this one may pass», and all of them
/// die with the browser context. A reader that opens a fresh context per page therefore arrives as a stranger
/// every time and is challenged every time — slower for us and noisier for the site.
/// <para>
/// A persistent profile would be the obvious answer and is not available: <c>launchServer</c> takes no user
/// data directory, so a browser reached over <c>ws://</c> cannot be given one. What is available is the
/// storage state — cookies and local storage, read out of one context and put into the next — and it carries
/// exactly the part that matters.
/// </para>
/// <para>
/// The shipped implementation keeps it in memory for
/// <see cref="ScrapingOptions.RememberSiteFor"/> and forgets a host nobody returns to. A deployment that wants
/// it to survive a restart writes its own and registers that instead: the state is a string, and where it is
/// written is a decision about the deployment's own storage, not about this library. Whoever does that should
/// know what they are keeping — it is the site's permission, and it identifies the visitor.
/// </para>
/// </remarks>
public interface IKeepsakes
{
    /// <summary>What this host left with us, or nothing when it left nothing.</summary>
    string? Of(string host);

    /// <summary>Keeps what this host said, replacing whatever it said before.</summary>
    /// <remarks>
    /// The newest answer wins outright rather than being merged: the state is the browser's own summary of the
    /// context it came from, and half of one summary beside half of another is not a state any browser held.
    /// </remarks>
    void Keep(string host, string state);
}
