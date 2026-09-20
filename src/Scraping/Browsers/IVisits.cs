using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Opens browser pages already dressed as the identity they claim.
/// </summary>
/// <remarks>
/// The preparation a page needs before it is safe to show to a site — the context built from the identity, the
/// stealth script, the client-hint metadata, the navigation timeouts — is the same whether the page is going to
/// be read once or driven through a form. Keeping it in one place is not tidiness: the defect that prompted this
/// seam was a context that announced one browser in its headers while the page reported another in script,
/// because the two were set in two places and drifted apart.
/// </remarks>
public interface IVisits
{
    /// <summary>Rents a browser and hands back a page prepared under the given identity.</summary>
    /// <remarks>
    /// The appetite is stated rather than defaulted, so that a caller driving a page through its own scripts
    /// says so where it can be read beside the code that depends on it.
    /// </remarks>
    /// <param name="identity">The browser this visit presents itself as.</param>
    /// <param name="appetite">How much of the page is fetched.</param>
    /// <param name="target">Where the visit is going; only the host matters, and it is what the site is remembered by.</param>
    /// <param name="ct">Cancels opening the context.</param>
    /// <remarks>
    /// The address is here so that what the site left last time can be given back to it: cookies and local
    /// storage are kept per host, and a cleared challenge lives in exactly those. Without it every visit
    /// arrives as a stranger, which is what this library did until 2026-09-20.
    /// </remarks>
    Task<IVisit> OpenAsync(WebIdentity identity, Appetite appetite, Uri target, CancellationToken ct = default);
}
