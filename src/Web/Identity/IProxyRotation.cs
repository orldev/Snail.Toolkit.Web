namespace Snail.Toolkit.Web.Identity;

/// <summary>
/// Hands out the configured proxies in turn.
/// </summary>
public interface IProxyRotation
{
    /// <summary>The exit this host always leaves by, or nothing when none is configured.</summary>
    /// <remarks>
    /// The other half of a steady identity: a challenge cleared from one address is cleared for that address,
    /// so a host kept to one browser has to be kept to one exit as well.
    /// </remarks>
    ProxyEndpoint? For(string host);

    /// <summary>Takes the next proxy, or <see langword="null"/> when none are configured.</summary>
    ProxyEndpoint? Next();
}
