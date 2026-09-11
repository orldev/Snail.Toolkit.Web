using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// A prepared page and the identity it presents, held until disposal returns the browser to the pool.
/// </summary>
public interface IVisit : IAsyncDisposable
{
    /// <summary>The page to navigate or drive.</summary>
    IPage Page { get; }

    /// <summary>The identity the page presents, needed to explain a failure after the fact.</summary>
    WebIdentity Identity { get; }
}
