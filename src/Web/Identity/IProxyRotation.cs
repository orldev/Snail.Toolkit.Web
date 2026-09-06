namespace Snail.Toolkit.Web.Identity;

/// <summary>
/// Hands out the configured proxies in turn.
/// </summary>
public interface IProxyRotation
{
    /// <summary>Takes the next proxy, or <see langword="null"/> when none are configured.</summary>
    ProxyEndpoint? Next();
}
