using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Identity;
using Snail.Toolkit.Web.Scraping.Stealth;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Rents a browser, builds the context an identity implies and returns a page ready to be shown to a site.
/// </summary>
/// <remarks>
/// Ownership is the delicate part: the lease and the context are acquired one after the other, and anything that
/// throws in between would strand a browser in the pool for the life of the process. Both are released here on
/// any failure, and afterwards they belong to the returned <see cref="IVisit"/>.
/// </remarks>
public sealed class Visits(
    IBrowserPool pool,
    IStealthScript stealth,
    IClientHints hints,
    IKeepsakes keepsakes,
    IOptions<ScrapingOptions> options) : IVisits
{
    private readonly ScrapingOptions _options = options.Value;

    /// <inheritdoc />
    /// <remarks>
    /// The context is new every time — a context is what holds one page's work — but it is no longer EMPTY
    /// every time: what the host left on the last visit goes back into it, and what this visit ends with is
    /// kept for the next. A challenge cleared once is then cleared for as long as the cookie it gave lives,
    /// which is the difference between a returning visitor and a stranger who is challenged again.
    /// </remarks>
    public async Task<IVisit> OpenAsync(
        WebIdentity identity,
        Appetite appetite,
        Uri target,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(target);

        var traits = BrowserTraits.For(identity);
        var lease = await pool.RentAsync(ct);
        IBrowserContext? context = null;

        try
        {
            context = await lease.Browser.NewContextAsync(
                BrowserContexts.For(
                    identity,
                    traits,
                    _options.Engine,
                    _options.IgnoreCertificateErrors,
                    keepsakes.Of(target.Host)));

            context.SetDefaultNavigationTimeout(_options.NavigationTimeoutMs);
            context.SetDefaultTimeout(_options.NavigationTimeoutMs);

            await context.AddInitScriptAsync(stealth.For(identity));

            if (appetite is Appetite.DocumentOnly)
                await context.RouteAsync("**/*", TheDocumentAloneAsync);

            var page = await context.NewPageAsync();

            await hints.AlignAsync(page, identity, traits, ct);

            return new Visit(lease, context, page, identity, keepsakes, target.Host);
        }
        catch
        {
            if (context is not null)
                await context.DisposeAsync();

            await lease.DisposeAsync();

            throw;
        }
    }

    /// <summary>Lets the document through and refuses everything it goes on to ask for.</summary>
    /// <remarks>
    /// Installed on the context rather than the page so that it also covers frames, and before the first
    /// navigation because a route added afterwards would miss the very request it exists for.
    /// </remarks>
    private static async Task TheDocumentAloneAsync(IRoute route)
    {
        if (route.Request.ResourceType is "document")
        {
            await route.ContinueAsync();

            return;
        }

        await route.AbortAsync();
    }
}
