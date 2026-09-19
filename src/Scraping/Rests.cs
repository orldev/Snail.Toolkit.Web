using System.Globalization;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Reads the gap a site asked to be left alone for out of what it answered with.
/// </summary>
/// <remarks>
/// Written once and read by everything that drives a browser. Until 2026-09-20 only the page reader read the
/// header: the errand and the call to a site's own service carried the status and never the number, so a
/// service that said "come back in thirty seconds" in the middle of a form told one caller and nobody else —
/// <c>Attempts</c> writes the gap into the per-host gate, where it binds every later caller, and it had nothing
/// to write. That is the twelfth item on the consumer's list.
/// <para>
/// Headers rather than a response, so that the reading can be checked without a browser, and the instant is
/// given rather than taken from a clock, so that a date in the header can be checked at all.
/// </para>
/// </remarks>
internal static class Rests
{
    /// <summary>The gap the answer asks for, in either form the header is written in.</summary>
    /// <remarks>
    /// Seconds are what a rate limiter sends and a date is what a cache sends. A date already in the past is
    /// read as no wait rather than a negative one, which is what a clock a few seconds out produces.
    /// </remarks>
    public static TimeSpan? AskedFor(IReadOnlyDictionary<string, string>? headers, DateTimeOffset now)
    {
        if (headers is null || !headers.TryGetValue("retry-after", out var asked))
            return null;

        return AskedFor(asked, now);
    }

    /// <summary>The same reading of one header value, for a caller that has the value and not the headers.</summary>
    public static TimeSpan? AskedFor(string? asked, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(asked))
            return null;

        if (int.TryParse(asked, CultureInfo.InvariantCulture, out var seconds))
            return seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;

        if (!DateTimeOffset.TryParse(asked, CultureInfo.InvariantCulture, out var until))
            return null;

        var gap = until - now;

        return gap > TimeSpan.Zero ? gap : null;
    }
}
