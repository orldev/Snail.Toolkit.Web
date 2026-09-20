using System.Net;
using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Checks that a configured exit actually reaches the handler an outgoing request leaves through.
/// </summary>
/// <remarks>
/// Worth testing offline, because the failure it guards against is invisible until it matters: the browser
/// honours <c>Web:Identity:Proxies</c> while every plain HTTP client ignores it, so a source reached both ways
/// is fetched from two different addresses and the refusal looks like the source rather than the route. No
/// proxy has to exist for this — the assertion is that the setting was carried, not that it works.
/// </remarks>
public class ProxiedTests
{
    [Fact]
    public void Handler_AConfiguredExit_LeavesThroughIt()
    {
        using var handler = Proxied.Handler(new StubRotation(new ProxyEndpoint("http://ru.example:3128")));

        var proxy = Assert.IsType<WebProxy>(handler.Proxy);

        Assert.True(handler.UseProxy);
        Assert.Equal("http://ru.example:3128/", proxy.Address!.ToString());
    }

    [Fact]
    public void Handler_AnExitThatWantsCredentials_CarriesThem()
    {
        var exit = new ProxyEndpoint("http://ru.example:3128") { Username = "user", Password = "секрет" };

        using var handler = Proxied.Handler(new StubRotation(exit));

        var proxy = Assert.IsType<WebProxy>(handler.Proxy);
        var given = Assert.IsType<NetworkCredential>(proxy.Credentials);

        Assert.Equal("user", given.UserName);
        Assert.Equal("секрет", given.Password);
    }

    /// <summary>With nothing configured a caller goes out the way it always did.</summary>
    /// <remarks>
    /// <c>UseProxy</c> is deliberately left alone rather than switched off. It defaults to true, meaning «use
    /// whatever proxy this machine is set up with», and forcing it false would quietly disable a proxy an
    /// operator had configured outside the process. Asserting that it stays true states that choice instead of
    /// letting a later reader think the check was merely relaxed to go green: the promise is that no exit of
    /// ours was added, not that the process was cut off from the network it already had.
    /// </remarks>
    [Fact]
    public void Handler_NoExitConfigured_GoesOutDirectly()
    {
        using var handler = Proxied.Handler(new StubRotation(null));

        Assert.Null(handler.Proxy);
        Assert.True(handler.UseProxy);
    }

    /// <summary>
    /// A caller that needs a handler of its own must keep what it was for.
    /// </summary>
    /// <remarks>
    /// One consumer carries a cookie container for a two-step search, another refuses redirects so a link check
    /// reports the first answer rather than the last. Routing them through a proxy must add to that, not replace
    /// it — which is why the exit is applied first and the caller tunes afterwards.
    /// </remarks>
    [Fact]
    public void Handler_ACallerThatTunesIt_KeepsBothTheExitAndTheTuning()
    {
        using var handler = Proxied.Handler(
            new StubRotation(new ProxyEndpoint("http://ru.example:3128")),
            tuned =>
            {
                tuned.AllowAutoRedirect = false;
                tuned.UseCookies = true;
            });

        Assert.True(handler.UseProxy);
        Assert.False(handler.AllowAutoRedirect);
        Assert.True(handler.UseCookies);
    }

    [Fact]
    public void Handler_AnyRequest_AsksForCompressedAnswers()
    {
        using var handler = Proxied.Handler(new StubRotation(null));

        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
    }

    private sealed class StubRotation(ProxyEndpoint? exit) : IProxyRotation
    {
        public ProxyEndpoint? For(string host) => exit;

        public ProxyEndpoint? Next() => exit;
    }
}
