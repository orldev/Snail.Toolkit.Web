namespace Snail.Toolkit.Web.Politeness;

/// <summary>
/// Paces requests so that no single host sees a burst.
/// </summary>
public interface IPoliteness
{
    /// <summary>Waits for this host's turn and holds it until the returned handle is disposed.</summary>
    /// <param name="url">The address about to be fetched; only its host matters.</param>
    /// <param name="requested">A gap the site itself asked for, when it asked for one.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <remarks>
    /// Limits are per host, so parallelism across a list of many sites is unaffected while one site never sees
    /// more than it allows. Request rate is the signal a site reads first, and the engines previously had no
    /// answer to it at all — every worker hit the same host as fast as the transport could load.
    /// <para>
    /// A gap the site asked for never shortens the configured one. Both are floors, so the wait is the longer
    /// of the two: a site asking for one second cannot talk a careful caller down to it.
    /// </para>
    /// </remarks>
    Task<IAsyncDisposable> EnterAsync(Uri url, TimeSpan? requested = null, CancellationToken ct = default);
}
