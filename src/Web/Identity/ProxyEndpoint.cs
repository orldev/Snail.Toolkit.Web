using System.Diagnostics.CodeAnalysis;

namespace Snail.Toolkit.Web.Identity;

/// <summary>
/// One proxy a request can be routed through.
/// </summary>
/// <param name="Server">The endpoint in the form a transport expects, scheme included.</param>
/// <remarks>
/// The scheme is part of <paramref name="Server"/> rather than being dropped during parsing. A previous
/// version built the endpoint as <c>host:port</c>, so a <c>socks5://</c> proxy was silently used as HTTP and
/// traffic went somewhere other than the configuration said.
/// </remarks>
public sealed record ProxyEndpoint(string Server)
{
    /// <summary>Gets the user name, when the proxy requires authentication.</summary>
    public string? Username { get; init; }

    /// <summary>Gets the password, when the proxy requires authentication.</summary>
    public string? Password { get; init; }

    /// <summary>Gets whether the proxy carries credentials.</summary>
    public bool IsAuthenticated => Username is not null;

    /// <summary>Reads a proxy written as <c>[scheme://][user:password@]host:port</c>.</summary>
    /// <remarks>
    /// Credentials are optional. An earlier parser demanded them, so an open proxy such as
    /// <c>http://10.0.0.1:8080</c> threw <see cref="FormatException"/> from the middle of a fetch. A missing
    /// scheme is read as <c>http</c>, which is what a bare <c>host:port</c> has always meant here. The split
    /// on the first colon only is deliberate: a password containing one is common, and splitting on every
    /// colon silently truncates it.
    /// </remarks>
    public static bool TryParse(string? value, [NotNullWhen(true)] out ProxyEndpoint? endpoint)
    {
        endpoint = null;

        var text = value?.Trim();

        if (string.IsNullOrEmpty(text))
            return false;

        if (!text.Contains("://", StringComparison.Ordinal))
            text = $"http://{text}";

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) || uri.Port <= 0)
            return false;

        endpoint = new ProxyEndpoint($"{uri.Scheme}://{uri.Host}:{uri.Port}")
        {
            Username = UserNameOf(uri.UserInfo),
            Password = PasswordOf(uri.UserInfo)
        };

        return true;
    }

    private static string? UserNameOf(string userInfo)
    {
        if (string.IsNullOrEmpty(userInfo))
            return null;

        var separator = userInfo.IndexOf(':', StringComparison.Ordinal);

        return Uri.UnescapeDataString(separator < 0 ? userInfo : userInfo[..separator]);
    }

    private static string? PasswordOf(string userInfo)
    {
        var separator = userInfo.IndexOf(':', StringComparison.Ordinal);

        return separator < 0 ? null : Uri.UnescapeDataString(userInfo[(separator + 1)..]);
    }

    /// <inheritdoc />
    /// <remarks>Credentials are deliberately not rendered: this value reaches logs.</remarks>
    public override string ToString() => IsAuthenticated ? $"{Server} (authenticated)" : Server;
}
