using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// What a site handed over and is worth handing back on the next visit: its cookies and its local storage.
/// </summary>
/// <remarks>
/// A challenge a site sets does not end when the page closes. Cloudflare answers a cleared challenge with
/// <c>cf_clearance</c>, a consent wall with a preference cookie, a registry with a session — all of them are
/// how the site says «this one may pass», and all of them die with the browser context. A reader that opens a
/// fresh context per page therefore arrives as a stranger every time and is challenged every time, which is
/// both slower for us and noisier for the site.
/// <para>
/// A PERSISTENT PROFILE WOULD BE THE OBVIOUS ANSWER AND IS NOT AVAILABLE: <c>launchServer</c> takes no user
/// data directory, and a browser reached over <c>ws://</c> cannot be given one — established while moving the
/// browser into a container. What is available is the storage state, which carries exactly the part that
/// matters: cookies and local storage, read out of a context and put into the next one.
/// </para>
/// <para>
/// Kept per HOST rather than per run, because that is what a cookie is about, and forgotten on the same rule
/// as everything else per host in this toolkit — an entry nobody has asked for in
/// <see cref="ScrapingOptions.RememberSiteFor"/> is dropped. Held in memory only: this is a cache of
/// permission, not a credential store, and a process that restarts is a visitor who cleared their cookies.
/// </para>
/// </remarks>
internal sealed class Keepsakes(IOptions<ScrapingOptions> options, TimeProvider clock) : IKeepsakes
{
    private readonly Remembered<Keepsake> _kept = Held(options, clock);

    /// <summary>How many hosts are currently remembered.</summary>
    internal int RememberedHosts => _kept.Count;

    /// <inheritdoc />
    public string? Of(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return _kept.TryGet(host, out var keepsake) ? keepsake.State : null;
    }

    /// <inheritdoc />
    public void Keep(string host, string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (string.IsNullOrWhiteSpace(state))
            return;

        _kept.Of(host, _ => new Keepsake(state)).State = state;
    }

    private static Remembered<Keepsake> Held(IOptions<ScrapingOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new Remembered<Keepsake>(options.Value.RememberSiteFor, (_, _) => true, clock);
    }

    /// <summary>One host's storage state, as the browser wrote it.</summary>
    private sealed class Keepsake(string state)
    {
        public string State { get; set; } = state;
    }
}
