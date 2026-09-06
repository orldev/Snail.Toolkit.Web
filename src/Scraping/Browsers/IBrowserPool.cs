namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// A pool of running browsers, launched on demand and closed when idle.
/// </summary>
public interface IBrowserPool : IAsyncDisposable
{
    /// <summary>Takes a live browser from the pool, waiting for a free one when all are in use.</summary>
    /// <remarks>
    /// The returned browser is checked for a live connection first, so a crashed process or a restarted
    /// container is replaced rather than handed out. An earlier implementation cached one browser and tested it
    /// for <see langword="null"/> only, so every request after a disconnect failed until the consuming process
    /// was restarted.
    /// </remarks>
    Task<IBrowserLease> RentAsync(CancellationToken ct = default);
}
