namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Why a page read failed, in the amount of detail a caller can act on.
/// </summary>
/// <param name="Kind">The classification to branch on.</param>
/// <param name="Message">A human-readable description of the failure.</param>
/// <remarks>
/// The original exception is kept on <see cref="Cause"/> instead of being flattened into
/// <paramref name="Message"/>. Reading a page fails for reasons that look alike in a log line and differ
/// completely in what they demand — a Cloudflare interstitial and a DNS failure both end a request, and only
/// one of them is worth retrying with a new proxy.
/// </remarks>
public sealed record PageFailure(FailureKind Kind, string Message)
{
    /// <summary>Gets the address the failure happened on.</summary>
    public Link? Address { get; init; }

    /// <summary>Gets the HTTP status the site answered with, when navigation produced a response.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Gets the exception this failure was translated from.</summary>
    public Exception? Cause { get; init; }

    /// <summary>Gets the identity that was in use, so a repeated block can be traced to a fingerprint.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Gets whether trying again with a different identity or proxy could succeed.</summary>
    /// <remarks>
    /// Retrying a <see cref="FailureKind.RobotsDisallowed"/> or an <see cref="FailureKind.Empty"/> answer only
    /// repeats the same work: the site already gave its final answer. The same is true of most client errors —
    /// a 404 is a fact about the URL, and retrying it three times with exponential backoff spends minutes to be
    /// told the same thing. A 429 is the exception, because it explicitly asks for a later attempt, as does
    /// anything the server blames on itself.
    /// </remarks>
    public bool IsTransient => Kind switch
    {
        FailureKind.Blocked
            or FailureKind.Timeout
            or FailureKind.BrowserUnavailable
            or FailureKind.Proxy => true,
        FailureKind.Navigation => StatusCode is null or 429 or >= 500,
        _ => false
    };

    /// <inheritdoc />
    public override string ToString()
        => StatusCode is null
            ? $"{Kind}: {Message}"
            : $"{Kind} ({StatusCode}): {Message}";
}
