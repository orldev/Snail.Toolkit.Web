using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Identity;

namespace Snail.Toolkit.Web.Scraping.Stealth;

/// <summary>
/// Makes the metadata a page can read agree with the client hints the context sends.
/// </summary>
public interface IClientHints
{
    /// <summary>Aligns <c>navigator.userAgentData</c> with the identity the context was opened under.</summary>
    Task AlignAsync(IPage page, WebIdentity identity, BrowserTraits traits, CancellationToken ct = default);
}
