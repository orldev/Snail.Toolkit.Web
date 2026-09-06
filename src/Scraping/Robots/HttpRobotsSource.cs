using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Scraping.Http;

namespace Snail.Toolkit.Web.Scraping.Robots;

/// <summary>
/// Downloads <c>robots.txt</c> over HTTP, through the proxy the rest of the visit uses.
/// </summary>
/// <remarks>
/// This is the one place a transport is needed to obey a site's rules, and it is deliberately not a browser
/// page — see <see cref="ProxiedHttp"/> for why, and for why the clients are held per proxy.
/// A site that cannot be asked is treated as permitting the fetch. RFC 9309 suggests the opposite for a server
/// error, but failing closed on a transient 502 would stop a crawl silently and look exactly like a bug; the
/// warning in the log is the deliberate trade.
/// </remarks>
public sealed class HttpRobotsSource(IOptions<RobotsOptions> options, ILogger<HttpRobotsSource> logger)
    : IRobotsSource, IDisposable
{
    private readonly RobotsOptions _options = options.Value;
    private readonly ProxiedHttp _http = new();

    /// <inheritdoc />
    public async Task<RobotsFile> ReadAsync(Uri origin, ProxyEndpoint? proxy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origin);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_options.Timeout);

        try
        {
            var response = await _http.For(proxy).GetAsync(new Uri(origin, "/robots.txt"), budget.Token);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                return RobotsFile.AllowAll;

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("robots.txt for {Origin} answered {Status}; treating the site as open",
                    origin, (int)response.StatusCode);

                return RobotsFile.AllowAll;
            }

            return RobotsFile.Parse(await response.Content.ReadAsStringAsync(budget.Token));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(exception, "robots.txt for {Origin} could not be read; treating the site as open",
                origin);

            return RobotsFile.AllowAll;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}
