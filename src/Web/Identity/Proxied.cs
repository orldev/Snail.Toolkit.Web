using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Snail.Toolkit.Web.Identity;

/// <summary>
/// Builds the handler an outgoing request leaves through, sending it via the configured exit when there is one.
/// </summary>
/// <remarks>
/// Here rather than in a transport package because every transport needs it and none of them owns it, and it
/// costs the core nothing: <see cref="SocketsHttpHandler"/> ships with the framework, so this adds no package
/// reference. Without it a consumer has two exits at once — the browser honours the configured proxies because
/// its context is built from the identity, while every plain <see cref="HttpClient"/> ignores them. A source
/// reached both ways is then fetched from two different addresses, which is worse than having no proxy at all:
/// half the requests are still refused and the reason looks like the source rather than the route.
/// <para>
/// The exit is chosen once, when the handler is built, and a pooled handler outlives many requests. With a
/// single proxy that is exactly right; with a list, the HTTP path takes one of them and stays on it rather than
/// rotating per request the way the browser does. Rotation here would mean a handler per request, which throws
/// away connection pooling — a worse trade than the one it fixes.
/// </para>
/// </remarks>
public static class Proxied
{
    /// <summary>The handler for one exit, tuned by whatever the caller needs on top of it.</summary>
    public static SocketsHttpHandler Handler(ProxyEndpoint? exit, Action<SocketsHttpHandler>? tune = null)
    {
        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All };

        if (exit is not null)
        {
            handler.Proxy = new WebProxy(exit.Server)
            {
                Credentials = exit.IsAuthenticated
                    ? new NetworkCredential(exit.Username, exit.Password)
                    : null
            };

            handler.UseProxy = true;
        }

        tune?.Invoke(handler);

        return handler;
    }

    /// <summary>The handler for the next exit of a rotation, or a direct one when none is configured.</summary>
    public static SocketsHttpHandler Handler(IProxyRotation proxies, Action<SocketsHttpHandler>? tune = null)
    {
        ArgumentNullException.ThrowIfNull(proxies);

        return Handler(proxies.Next(), tune);
    }

    /// <summary>The same handler, resolved from the container where a registration hands one over.</summary>
    /// <remarks>
    /// The shape <c>ConfigurePrimaryHttpMessageHandler</c> asks for, so a named client is proxied by one line
    /// at its registration rather than by a factory of the consumer's own.
    /// </remarks>
    public static HttpMessageHandler Handler(IServiceProvider provider, Action<SocketsHttpHandler>? tune = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return Handler(provider.GetRequiredService<IProxyRotation>(), tune);
    }
}
