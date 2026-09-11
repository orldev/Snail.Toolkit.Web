using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Holds a page together with the context and lease that back it, and releases all three in order.
/// </summary>
internal sealed class Visit(
    IBrowserLease lease,
    IBrowserContext context,
    IPage page,
    WebIdentity identity) : IVisit
{
    /// <inheritdoc />
    public IPage Page { get; } = page;

    /// <inheritdoc />
    public WebIdentity Identity { get; } = identity;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync();
        await lease.DisposeAsync();
    }
}
