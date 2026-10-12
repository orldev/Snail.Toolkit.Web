using Microsoft.Extensions.Logging;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// What reading, picturing and crawling pages say about the browser and the sites.
/// </summary>
/// <remarks>
/// The browser pool, the attempts and the crawl report here; a page that could not be read is reported to the caller in its outcome, and the log only adds what the outcome cannot carry.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message =
        "robots.txt forbids {Url}")]
    internal static partial void Forbidden(ILogger logger, Link url);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message =
        "{Host} asked to be left alone for {Gap}; holding it for every caller, not just this one.")]
    internal static partial void HeldForEveryone(ILogger logger, string host, TimeSpan gap);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message =
        "Retry {Attempt} after {Failure}")]
    internal static partial void Retrying(ILogger logger, int attempt, PageFailure? failure);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message =
        "Human simulation ran out of its {Budget} budget")]
    internal static partial void OutOfBudget(ILogger logger, TimeSpan budget);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message =
        "Human simulation stopped early")]
    internal static partial void StoppedEarly(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message =
        "{Count} browser arguments are configured but a browser server is in use, and a connection "
        + "carries no command line: the server was started with its own. Measured consequence: with "
        + "'--disable-blink-features=AutomationControlled' among them, a connected browser still reports "
        + "navigator.webdriver as true. Pass the arguments to the server's own launch command instead.")]
    internal static partial void ArgumentsIgnored(ILogger logger, int count);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message =
        "The browser lost its connection and is being replaced")]
    internal static partial void ConnectionLost(ILogger logger);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message =
        "Browser is ready: {Engine}, {Mode}, up to {Pages} page(s) at once")]
    internal static partial void BrowserReady(ILogger logger, Engine engine, string mode, int pages);

    [LoggerMessage(EventId = 9, Level = LogLevel.Debug, Message =
        "The browser refused to close cleanly")]
    internal static partial void CloseRefused(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message =
        "The idle reaper ended badly; the pool is closing anyway")]
    internal static partial void ReaperFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message =
        "The browser was idle for {Timeout} and is being closed")]
    internal static partial void IdleClosing(ILogger logger, TimeSpan timeout);

    [LoggerMessage(EventId = 12, Level = LogLevel.Error, Message =
        "The idle browser could not be closed")]
    internal static partial void IdleCloseFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message =
        "Crawl of {Start} read {Read} pages and failed on {Failed} in {Elapsed}, ending as {Stop}")]
    internal static partial void CrawlEnded(ILogger logger, Link start, int read, int failed, TimeSpan elapsed, CrawlStop stop);

    [LoggerMessage(EventId = 14, Level = LogLevel.Error, Message =
        "Unclassified failure while reading {Url}")]
    internal static partial void ReadFailed(ILogger logger, Exception exception, Link url);

    [LoggerMessage(EventId = 15, Level = LogLevel.Debug, Message =
        "Network never went idle for {Url}; continuing with the loaded DOM")]
    internal static partial void NeverIdle(ILogger logger, string url);

    [LoggerMessage(EventId = 16, Level = LogLevel.Debug, Message =
        "Page error on {Url}: {Error}")]
    internal static partial void PageError(ILogger logger, Link url, string error);

    [LoggerMessage(EventId = 17, Level = LogLevel.Error, Message =
        "Unclassified failure while picturing {Url}")]
    internal static partial void PictureFailed(ILogger logger, Exception exception, Link url);

    [LoggerMessage(EventId = 18, Level = LogLevel.Debug, Message =
        "{Url} never finished loading; picturing what is drawn")]
    internal static partial void NeverLoaded(ILogger logger, string url);

    [LoggerMessage(EventId = 19, Level = LogLevel.Warning, Message =
        "robots.txt for {Origin} answered {Status}; treating the site as open")]
    internal static partial void RobotsAnswered(ILogger logger, Uri origin, int status);

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning, Message =
        "robots.txt for {Origin} could not be read; treating the site as open")]
    internal static partial void RobotsUnreadable(ILogger logger, Exception exception, Uri origin);

    [LoggerMessage(EventId = 21, Level = LogLevel.Debug, Message =
        "Sitemap {Address} answered {Status}")]
    internal static partial void SitemapAnswered(ILogger logger, Uri address, int status);

    [LoggerMessage(EventId = 22, Level = LogLevel.Warning, Message =
        "Sitemap {Address} could not be read")]
    internal static partial void SitemapUnreadable(ILogger logger, Exception exception, Uri address);

    [LoggerMessage(EventId = 23, Level = LogLevel.Warning, Message =
        "A sitemap reached the {Limit} byte ceiling and was cut, so it will not parse; "
        + "raise '{Section}:{Setting}' if the site really publishes one that large")]
    internal static partial void SitemapCut(ILogger logger, int limit, string section, string setting);

    [LoggerMessage(EventId = 24, Level = LogLevel.Information, Message =
        "{Origin} published no sitemap worth reading; following its links instead")]
    internal static partial void NoSitemap(ILogger logger, Uri origin);

    [LoggerMessage(EventId = 25, Level = LogLevel.Warning, Message =
        "Client hints stay as the browser built them while the headers claim {Brand} {Version}; the two "
        + "disagree and that disagreement is itself detectable.")]
    internal static partial void HintsDisagree(ILogger logger, Exception exception, string brand, string version);
}
