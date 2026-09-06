using System.Collections.Concurrent;
using System.Net;
using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping.Http;

/// <summary>
/// One HTTP client per proxy, for the few documents that are read without a browser.
/// </summary>
/// <remarks>
/// <c>robots.txt</c> and sitemaps are the two files a page reader needs and a browser is wrong for: rendering
/// a text or XML file costs a browser context and a navigation budget to read a few hundred bytes, and in the
/// XML case the browser hands back its own tree view instead of the document.
/// <para>
/// A client is kept per proxy for the lifetime of the process rather than taken from
/// <c>IHttpClientFactory</c>, which rotates handlers on a timer for DNS freshness and knows nothing about
/// proxies. That is what makes this disposable.
/// </para>
/// </remarks>
internal sealed class ProxiedHttp : IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new(StringComparer.Ordinal);

    public HttpClient For(ProxyEndpoint? proxy)
        => _clients.GetOrAdd(proxy?.Server ?? string.Empty, _ => Build(proxy));

    private static HttpClient Build(ProxyEndpoint? proxy)
    {
        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All };

        if (proxy is not null)
        {
            handler.Proxy = new WebProxy(proxy.Server)
            {
                Credentials = proxy.IsAuthenticated
                    ? new NetworkCredential(proxy.Username, proxy.Password)
                    : null
            };
            handler.UseProxy = true;
        }

        return new HttpClient(handler, disposeHandler: true);
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();

        _clients.Clear();
    }
}
