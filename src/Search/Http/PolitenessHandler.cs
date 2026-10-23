using Snail.Toolkit.Web.Politeness;

namespace Snail.Toolkit.Web.Search.Http;

/// <summary>
/// Holds the per-host gate for the length of one request, and hands the gate what the site answers back.
/// </summary>
/// <remarks>
/// This replaces a hand-rolled throttling handler that kept its own semaphore and its own per-host clock, so
/// the browser side and the HTTP side paced themselves independently and a host could be hit by both at once.
/// The gate is the core's, and every transport now waits at the same one.
/// The turn is held until the response headers arrive, not until the body is read: a slow download must not
/// keep the next request to that host waiting, and the gap that matters to a site is between requests.
/// <para>
/// A site asking to be left alone is written into that same gate. Until 2026-09-14 only the browser side did
/// this — <c>Rest</c> had exactly one caller — so a <c>429</c> answered to a search engine slowed nothing, and
/// a browser visit to the same host walked straight into a site that had just said it had had enough.
/// </para>
/// </remarks>
public sealed class PolitenessHandler(IPoliteness politeness) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestUri is null)
            return await base.SendAsync(request, cancellationToken);

        await using var turn = await politeness.EnterAsync(request.RequestUri, ct: cancellationToken);

        var answered = await base.SendAsync(request, cancellationToken);

        if (RestAskedFor(answered) is { } gap)
            politeness.Rest(request.RequestUri, gap);

        return answered;
    }

    /// <summary>The gap a response asks to be left alone for, in either form the header is written in.</summary>
    /// <remarks>
    /// Read by <see cref="Rests"/>, the one reading the toolkit has, since 2026-10-23. The instant is the system's
    /// here, as it was: this handler is built by its public constructor, and a clock of its own would be a change
    /// to that surface for a branch whose only effect is how long a host is held — a date is rare, and a clock a
    /// few seconds out is read as no gap rather than a negative one.
    /// </remarks>
    private static TimeSpan? RestAskedFor(HttpResponseMessage answered)
        => Rests.AskedFor(answered.Headers.RetryAfter?.ToString(), DateTimeOffset.UtcNow);
}
