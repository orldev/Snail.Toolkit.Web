using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// Permission to have one page open on the pooled browser, until it is disposed.
/// </summary>
/// <remarks>
/// It used to be exclusive use of a whole browser process, which is why the toolkit could never read more
/// pages at once than it ran browsers — one, at the defaults. A context costs a quarter of a process and opens
/// in a third of the time (measured by <c>probes/contexts</c> on 2026-09-14), so what is worth rationing is
/// pages rather than processes.
/// <para>
/// Disposing gives the permission back. Failing to dispose removes it from circulation permanently, so a lease
/// belongs in an <c>await using</c> and nowhere else.
/// </para>
/// </remarks>
public interface IBrowserLease : IAsyncDisposable
{
    /// <summary>Gets the leased browser.</summary>
    IBrowser Browser { get; }
}
