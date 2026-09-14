using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// How a named client's requests actually leave the process.
/// </summary>
/// <remarks>
/// A registration can be read and still be wrong about this: the handler a request is finally made by is
/// assembled from several calls in several packages, and the one that decides the exit is the one nobody
/// writes — its absence is what sent two of the three roads out directly while the browser was proxied.
/// Walking the built chain answers what the caller will do rather than what the registration says.
/// <para>
/// Everything here is public API. <see cref="DelegatingHandler.InnerHandler"/> is how the chain is walked and
/// <see cref="IWebProxy.GetProxy"/> is how the destination is decided, so nothing is read by reflection and
/// nothing depends on the order the handlers were added in.
/// </para>
/// </remarks>
internal static class Outgoing
{
    /// <summary>The handler at the end of a named client's chain — the one that opens the connection.</summary>
    public static HttpMessageHandler HandlerOf(IServiceProvider provider, string client)
    {
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(client);

        while (handler is DelegatingHandler forwarding && forwarding.InnerHandler is { } inner)
            handler = inner;

        return handler;
    }

    /// <summary>The address a request to <paramref name="destination"/> is sent to, or null when it goes direct.</summary>
    public static Uri? ExitOf(IServiceProvider provider, string client, Uri destination)
        => HandlerOf(provider, client) switch
        {
            SocketsHttpHandler sockets when sockets.UseProxy => Chosen(sockets.Proxy, destination),
            HttpClientHandler classic when classic.UseProxy => Chosen(classic.Proxy, destination),
            _ => null
        };

    private static Uri? Chosen(IWebProxy? proxy, Uri destination)
        => proxy is null || proxy.IsBypassed(destination) ? null : proxy.GetProxy(destination);
}
