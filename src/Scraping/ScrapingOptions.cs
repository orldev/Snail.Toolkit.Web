namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// How browsers are launched and how hard a page is tried.
/// </summary>
/// <remarks>
/// Bound through the standard options pattern and validated when the host starts, so a setting that cannot
/// work refuses to bring the process up instead of surfacing on the first URL. Identity and proxies are not
/// here: they belong to every transport, so they live in the core's own section.
/// </remarks>
public sealed class ScrapingOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Scraping";

    /// <summary>Gets or sets the WebSocket endpoint of a Playwright browser server to connect to.</summary>
    /// <remarks>
    /// Empty launches a browser in this process. A value must be the <c>ws://</c> endpoint a
    /// <c>playwright run-server</c> prints — not a proxy address, which is what an earlier name invited
    /// callers to put here.
    /// </remarks>
    public string? BrowserServer { get; set; }

    /// <summary>Gets or sets which browser engine pages are read with.</summary>
    /// <remarks>
    /// Chromium by default, because everything the toolkit aligns is aligned for it: client hints are set over
    /// CDP, which no other engine answers, and the launch switches are Chromium's own. Firefox earns its place
    /// where the browser must not announce itself — <c>navigator.webdriver</c> is compiled into Playwright's
    /// Firefox and no preference turns it off — and such a build is reached through <see cref="BrowserServer"/>,
    /// since a launched Firefox is Playwright's own and carries the announcement.
    /// </remarks>
    public Engine Engine { get; set; } = Engine.Chromium;

    /// <summary>Gets or sets whether launched browsers run without a window.</summary>
    public bool Headless { get; set; } = true;

    /// <summary>Gets or sets whether Chromium keeps its sandbox.</summary>
    /// <remarks>
    /// Scraping opens documents chosen by someone else, which is the exact threat the renderer sandbox exists
    /// for. An earlier default passed <c>--no-sandbox</c> and <c>--disable-web-security</c>, so a renderer
    /// exploit on any scraped page ran with the privileges of the host process. Turn this off only where the
    /// container already provides isolation and cannot offer the kernel features Chromium needs.
    /// </remarks>
    public bool Sandboxed { get; set; } = true;

    /// <summary>Gets or sets whether TLS certificate errors are ignored.</summary>
    /// <remarks>
    /// Was unconditionally on before, which hides a misconfigured MITM proxy and accepts any certificate a
    /// network attacker presents. Turn it on only where a proxy you control terminates TLS.
    /// </remarks>
    public bool IgnoreCertificateErrors { get; set; }

    /// <summary>Gets or sets how many pages may be open at once across all hosts.</summary>
    /// <remarks>
    /// The only ceiling there is, and that is the change made on 2026-09-14. There used to be two — this one,
    /// which sized the workers, and <c>MaxBrowsers</c>, which sized the pool — and the effective parallelism
    /// was the smaller of them, which at the defaults was ONE while this said three. A setting that promises
    /// what another setting silently withdraws is worse than no setting.
    /// <para>
    /// What it costs is memory, and the number to raise it by is measurable rather than guessable. Measured on
    /// 2026-09-14 by <c>probes/contexts</c>: on stock Playwright the first page costs 284 MB and every further
    /// one 70, with nothing degrading up to sixteen; on our own browser image, on a machine emulating another
    /// architecture, 178 MB a page. Run that probe against the image a deployment actually uses and divide the
    /// memory it can spare. Four is a default that fits a small container, not a recommendation.
    /// </para>
    /// <para>
    /// Across all hosts is the whole of it. Reading many pages of one site — a crawl, or a batch of links from
    /// one domain — is paced by <c>Web:Politeness:MaxConcurrentPerHost</c> instead, and raising this alone
    /// only adds workers that queue at the same per-host gate.
    /// </para>
    /// </remarks>
    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>Gets or sets the navigation budget in milliseconds.</summary>
    public int NavigationTimeoutMs { get; set; } = 45_000;

    /// <summary>Gets or sets whether to wait for network activity to settle after the DOM is ready.</summary>
    /// <remarks>
    /// Off by default. Any site with ads, analytics beacons, long polling or a websocket never reaches network
    /// idle, so waiting for it spent the entire navigation budget on every such page and then reported a
    /// timeout — the slowest possible way to produce nothing.
    /// </remarks>
    public bool WaitForNetworkIdle { get; set; }

    /// <summary>Gets or sets how many times a transient failure is retried.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Gets or sets how long what a site left with us is kept when nobody goes back to it.</summary>
    /// <remarks>
    /// A bound on memory rather than on the cookies themselves: each cookie carries its own expiry inside the
    /// state, and the browser drops the expired ones when the state is restored. What this decides is how long
    /// a host nobody has visited keeps taking up room. Zero switches the memory off entirely, and then every
    /// visit arrives as a stranger — which is what this library did until 2026-09-20.
    /// </remarks>
    public TimeSpan RememberSiteFor { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Gets or sets whether a refusal that names no gap is one of the failures worth repeating.</summary>
    /// <remarks>
    /// On, because a wall is often a fingerprint and the next attempt draws a different one — that is what
    /// makes a retry worth making at all, and a test holds the crawl to it. Off for a deployment whose target
    /// closes over FREQUENCY, where every further request only confirms the refusal and spends the budget.
    /// <para>
    /// MEASURED on 2026-09-20 on these defaults: a page that answers costs one request, one turn at the gate
    /// and 14 ms; a wall costs four of each and 17.7 seconds of backoff. On a host paced at thirty seconds
    /// those four turns are two minutes for one page, so a site that refuses everything turns a forty-minute
    /// crawl of some two hundred and sixty pages into one of sixty-five. That is the fourteenth item on the
    /// consumer's list, and it is a setting rather than a new default because which of the two a site is
    /// cannot be known from here — the engines of this toolkit, whose refusals are measured to be about
    /// frequency, do not repeat them at all.
    /// </para>
    /// </remarks>
    public bool RepeatsRefusals { get; set; } = true;

    /// <summary>Gets or sets the base delay between retries.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets how long an unused browser is kept before it is closed.</summary>
    public TimeSpan BrowserIdleTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>Gets or sets how many characters of extracted text reach the caller.</summary>
    /// <remarks>
    /// A rendered page of five megabytes fills an agent's context window before it can read a word. Zero means
    /// no ceiling; anything above it is cut and the cut is stated in the text itself. The markup is never cut:
    /// a ceiling on it would end the document in the middle of a tag and silently lose every link past there,
    /// which is exactly what a browser-backed search engine reads the markup for.
    /// </remarks>
    public int MaxContentCharacters { get; set; }

    /// <summary>Gets or sets extra Chromium command line switches.</summary>
    /// <remarks>Ignored when <see cref="BrowserServer"/> is set: the server owns its own command line.</remarks>
    public List<string> BrowserArguments { get; set; } = [];
}
