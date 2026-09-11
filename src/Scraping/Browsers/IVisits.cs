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
    Task<IVisit> OpenAsync(WebIdentity identity, CancellationToken ct = default);
}
