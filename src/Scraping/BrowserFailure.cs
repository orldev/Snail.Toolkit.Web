using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Reads what a browser threw as the kind of failure a caller can act on.
/// </summary>
/// <remarks>
/// Matching on the message is not a shortcut: it is the only route Playwright leaves open. Version 1.62 exposes
/// exactly one public exception type, <see cref="PlaywrightException"/> — its <c>TargetClosedException</c> is
/// declared <c>internal</c> and cannot be named by a consumer, and no timeout type exists at all. A locator wait
/// is the one place that raises <see cref="TimeoutException"/> from <c>System</c>; navigation never does, so
/// catching that alone once left every navigation timeout falling through to a catch-all.
/// <para>
/// Shared rather than private to the reader, because the kind is what decides whether another attempt is worth
/// making. The reader classified and the two that drive pages did not: everything a browser threw at them
/// became <see cref="FailureKind.Unexpected"/>, which is not transient, so a browser that had lost its
/// connection was indistinguishable from a selector that names nothing and no retry could tell them apart.
/// </para>
/// </remarks>
public static class BrowserFailure
{
    /// <summary>Reads one exception from a browser as a failure.</summary>
    public static PageFailure Of(Exception exception, Link address, WebIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(identity);

        return new PageFailure(KindOf(exception), exception.Message)
        {
            Address = address,
            Cause = exception,
            UserAgent = identity.UserAgent
        };
    }

    /// <summary>Answers whether this is an exception a browser is expected to raise.</summary>
    /// <remarks>
    /// The filter every caller needs, written once. Anything else is a defect in this library rather than an
    /// answer from the site, and is left to escape.
    /// </remarks>
    public static bool IsBrowsers(Exception exception) => exception is PlaywrightException or TimeoutException;

    private static FailureKind KindOf(Exception exception) => exception switch
    {
        TimeoutException => FailureKind.Timeout,
        PlaywrightException playwright => KindOf(playwright.Message),
        _ => FailureKind.Unexpected
    };

    /// <summary>Reads what Playwright said into the kind a caller branches on.</summary>
    /// <remarks>
    /// A broken WebSocket is the browser going away, not a page that would not load. Until 2026-09-20 it read
    /// as <see cref="FailureKind.Navigation"/> — a statement about the SITE — and a measurement on 16.09.2026
    /// shows why that matters: one heavy page killed Firefox inside the container, the Playwright server closed
    /// with it, and every caller saw <c>WebSocket error: socket hang up</c> on whatever page it happened to be
    /// reading. A reader looking at that line goes to the site; the thing to look at was the browser.
    /// </remarks>
    private static FailureKind KindOf(string message) => message switch
    {
        _ when message.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
            => FailureKind.Timeout,
        _ when message.Contains("Target closed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Browser closed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("has been closed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("has disconnected", StringComparison.OrdinalIgnoreCase)
               || message.Contains("WebSocket error", StringComparison.OrdinalIgnoreCase)
            => FailureKind.BrowserUnavailable,
        _ when message.Contains("ERR_PROXY", StringComparison.Ordinal)
               || message.Contains("ERR_TUNNEL_CONNECTION_FAILED", StringComparison.Ordinal)
            => FailureKind.Proxy,
        _ => FailureKind.Navigation
    };
}
