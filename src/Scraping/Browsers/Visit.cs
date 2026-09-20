using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Holds a page together with the context and lease that back it, and releases all three in order.
/// </summary>
/// <remarks>
/// What the site left — its cookies and local storage — is kept on the way out, so the next visit to that host
/// arrives as the visitor it was last time rather than as a stranger.
/// </remarks>
internal sealed class Visit(
    IBrowserLease lease,
    IBrowserContext context,
    IPage page,
    WebIdentity identity,
    IKeepsakes keepsakes,
    string host) : IVisit
{
    /// <inheritdoc />
    public IPage Page { get; } = page;

    /// <inheritdoc />
    public WebIdentity Identity { get; } = identity;

    /// <inheritdoc />
    /// <remarks>
    /// The browser goes back whatever the context does. Closing a context reaches the browser server over a
    /// socket and fails at the most ordinary moment there is — the connection dropped while the page was open —
    /// and until 2026-09-14 that refusal left the lease undisposed. The slot then left the pool for the life of
    /// the process, and at the default of one browser that is the whole pool: the next read waits forever on
    /// something nobody holds, without an error anywhere to say so.
    /// <para>
    /// The refusal is still raised. A context that would not close is worth hearing about — it is the caller's
    /// page that is still open somewhere — and swallowing it here would trade a visible failure for a leak.
    /// </para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await KeptAsync();

        try
        {
            await context.DisposeAsync();
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    /// <summary>Keeps what the site left, while there is still a context to read it from.</summary>
    /// <remarks>
    /// Here rather than in whoever asked for the visit, because the state can only be read while the context
    /// is open. A browser that went away in the middle takes it with it, and that is not worth an exception:
    /// the visit is over either way and the next one simply arrives as a stranger, which is what every visit
    /// did until 2026-09-20.
    /// </remarks>
    private async Task KeptAsync()
    {
        try
        {
            keepsakes.Keep(host, await context.StorageStateAsync());
        }
        catch (Exception refusal) when (BrowserFailure.IsBrowsers(refusal))
        {
        }
    }
}
