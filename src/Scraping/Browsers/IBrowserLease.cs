using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Exclusive use of one pooled browser until it is disposed.
/// </summary>
/// <remarks>
/// Disposing returns the browser to the pool. Failing to dispose removes it from circulation permanently, so a
/// lease belongs in an <c>await using</c> and nowhere else.
/// </remarks>
public interface IBrowserLease : IAsyncDisposable
{
    /// <summary>Gets the leased browser.</summary>
    IBrowser Browser { get; }
}
