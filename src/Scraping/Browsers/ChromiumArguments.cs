namespace Snail.Toolkit.Web.Scraping.Browsers;

/// <summary>
/// The Chromium command line a browser is launched with.
/// </summary>
/// <remarks>
/// Split into switches that hide automation and switches that weaken the browser. The second group is no longer
/// unconditional: scraping renders documents an attacker controls, so dropping the sandbox is a decision the
/// caller has to make deliberately through <see cref="ScrapingOptions.Sandboxed"/>.
/// </remarks>
public static class ChromiumArguments
{
    private static readonly string[] Stealth =
    [
        "--disable-blink-features=AutomationControlled",
        "--disable-features=IsolateOrigins,site-per-process,TranslateUI",
        "--disable-component-extensions-with-background-pages",
        "--disable-default-apps",
        "--disable-extensions",
        "--disable-ipc-flooding-protection",
        "--disable-popup-blocking",
        "--disable-prompt-on-repost",
        "--disable-renderer-backgrounding",
        "--disable-sync",
        "--force-color-profile=srgb",
        "--metrics-recording-only",
        "--no-first-run",
        "--hide-scrollbars",
        "--mute-audio"
    ];

    private static readonly string[] Unsandboxed =
    [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage"
    ];

    /// <summary>Builds the command line for the given settings.</summary>
    public static string[] For(ScrapingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Sandboxed
            ? [.. Stealth, .. options.BrowserArguments]
            : [.. Stealth, .. Unsandboxed, .. options.BrowserArguments];
    }
}
