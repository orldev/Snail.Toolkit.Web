using Snail.Toolkit.Web.Politeness;

namespace Snail.Toolkit.Web.Search.Http;

/// <summary>
/// Holds the per-host gate for the length of one request.
/// </summary>
/// <remarks>
/// This replaces a hand-rolled throttling handler that kept its own semaphore and its own per-host clock, so
/// the browser side and the HTTP side paced themselves independently and a host could be hit by both at once.
/// The gate is the core's, and every transport now waits at the same one.
/// The turn is held until the response headers arrive, not until the body is read: a slow download must not
/// keep the next request to that host waiting, and the gap that matters to a site is between requests.
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

        return await base.SendAsync(request, cancellationToken);
    }
}
