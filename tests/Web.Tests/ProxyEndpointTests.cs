using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the parser that used to be a regular expression demanding credentials.
/// </summary>
public class ProxyEndpointTests
{
    [Fact]
    public void TryParse_WithCredentials_ReadsServerAndCredentials()
    {
        var parsed = ProxyEndpoint.TryParse("http://alice:s3cret@10.0.0.1:8080", out var endpoint);

        Assert.True(parsed);
        Assert.Equal("http://10.0.0.1:8080", endpoint!.Server);
        Assert.Equal("alice", endpoint.Username);
        Assert.Equal("s3cret", endpoint.Password);
    }

    /// <summary>An open proxy used to throw <c>FormatException</c> from the middle of a fetch.</summary>
    [Fact]
    public void TryParse_WithoutCredentials_ReadsServerOnly()
    {
        var parsed = ProxyEndpoint.TryParse("http://10.0.0.1:8080", out var endpoint);

        Assert.True(parsed);
        Assert.Equal("http://10.0.0.1:8080", endpoint!.Server);
        Assert.False(endpoint.IsAuthenticated);
    }

    /// <summary>The scheme used to be discarded, so a SOCKS proxy was silently used as HTTP.</summary>
    [Fact]
    public void TryParse_SocksScheme_KeepsTheScheme()
    {
        var parsed = ProxyEndpoint.TryParse("socks5://user:pass@proxy.example:1080", out var endpoint);

        Assert.True(parsed);
        Assert.Equal("socks5://proxy.example:1080", endpoint!.Server);
    }

    [Fact]
    public void TryParse_WithoutScheme_AssumesHttp()
    {
        var parsed = ProxyEndpoint.TryParse("proxy.example:3128", out var endpoint);

        Assert.True(parsed);
        Assert.Equal("http://proxy.example:3128", endpoint!.Server);
    }

    /// <summary>
    /// A password containing a colon is common, and the version this replaces split on every colon, so the
    /// password silently arrived truncated and the proxy answered 407.
    /// </summary>
    [Fact]
    public void TryParse_PasswordContainingAColon_KeepsAllOfIt()
    {
        ProxyEndpoint.TryParse("http://alice:pa:ss@10.0.0.1:8080", out var endpoint);

        Assert.Equal("pa:ss", endpoint!.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a proxy at all")]
    [InlineData("http://")]
    public void TryParse_Malformed_Fails(string value)
    {
        var parsed = ProxyEndpoint.TryParse(value, out var endpoint);

        Assert.False(parsed);
        Assert.Null(endpoint);
    }

    [Fact]
    public void ToString_WithCredentials_HidesThem()
    {
        ProxyEndpoint.TryParse("http://alice:s3cret@10.0.0.1:8080", out var endpoint);

        var rendered = endpoint!.ToString();

        Assert.DoesNotContain("s3cret", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", rendered, StringComparison.Ordinal);
    }
}
