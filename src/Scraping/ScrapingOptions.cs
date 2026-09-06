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

    /// <summary>Gets or sets how many URLs may be read at once across all hosts.</summary>
    /// <remarks>
    /// Across all hosts is the whole of it. Reading many pages of one site — a crawl, or a batch of links from
    /// one domain — is paced by <c>Web:Politeness:MaxConcurrentPerHost</c> instead, and raising this alone
    /// only adds workers that queue at the same per-host gate.
    /// </remarks>
    public int MaxDegreeOfParallelism { get; set; } = 3;

    /// <summary>Gets or sets how many browser processes the pool may hold.</summary>
    /// <remarks>
    /// One browser served every worker before, so a single crash took the whole engine down until the process
    /// restarted, and one throttle value applied to every context at once.
    /// </remarks>
    public int MaxBrowsers { get; set; } = 1;

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
