using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Robots;

/// <summary>
/// Reads the <c>robots.txt</c> of one origin.
/// </summary>
/// <remarks>
/// The core parses and matches robots rules but never fetches them: downloading is the one part that needs a
/// transport, and binding the core to an HTTP client for it would put a client — and eventually a browser —
/// into every consumer of this package. Implementations live next to the transport that already exists.
/// </remarks>
public interface IRobotsSource
{
    /// <summary>Reads the rules an origin publishes, through <paramref name="proxy"/> when one is given.</summary>
    /// <remarks>
    /// The fetch goes through the same proxy the rest of the work uses. Reading <c>robots.txt</c> directly
    /// would announce the real origin of a crawl that every other request takes pains to route elsewhere.
    /// A site that cannot be asked is answered with <see cref="RobotsFile.AllowAll"/> rather than an
    /// exception: RFC 9309 suggests failing closed on a server error, but doing so on a transient 502 would
    /// stop a crawl silently and look exactly like a bug.
    /// </remarks>
    Task<RobotsFile> ReadAsync(Uri origin, ProxyEndpoint? proxy, CancellationToken ct = default);
}
