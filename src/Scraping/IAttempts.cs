using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Runs work against a site under one whole attempt, and makes another when the first was worth repeating.
/// </summary>
/// <remarks>
/// An attempt is the unit everything here is arranged around: a freshly drawn identity, the site's ruling on
/// that identity, its turn at the per-host gate, and then the work. Everything a retry could change sits inside
/// it — the identity is drawn per attempt, because that is what makes a second one worth making, and the robots
/// ruling and the gate follow that identity rather than one left over from an attempt that failed.
/// <para>
/// This exists because the sequence was written out by hand three times, and the three drifted. The page reader
/// carried a retry pipeline from the start; the site call was given one only after a measurement showed one run
/// in five lost to a navigation timeout; the agent had none at all, so a browser that went away in the middle of
/// a form lost the errand outright. One of them is now the sequence and the other two ask for it.
/// </para>
/// </remarks>
public interface IAttempts<TValue>
    where TValue : class
{
    /// <summary>Makes attempts at <paramref name="address"/> until one succeeds or the failure is final.</summary>
    /// <param name="address">Where the work is done; an address that is not absolute ends it before any attempt.</param>
    /// <param name="work">What to do once the site has been asked for permission and waited for.</param>
    /// <param name="ct">Cancels the attempts.</param>
    Task<Outcome<TValue, PageFailure>> AtAsync(
        Link address,
        Func<WebIdentity, Uri, CancellationToken, Task<Outcome<TValue, PageFailure>>> work,
        CancellationToken ct = default);
}
