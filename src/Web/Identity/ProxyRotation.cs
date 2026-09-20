using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Identity;

/// <summary>
/// Rotates through the proxies parsed once at start-up.
/// </summary>
/// <remarks>
/// Parsing happens in the constructor rather than per call. An earlier version ran a non-compiled regular
/// expression inside a <c>lock</c> on every page, which serialised every parallel worker on the hot path, and
/// threw <see cref="FormatException"/> from inside a retry loop when an entry was malformed. Rotation is a
/// single interlocked increment, so nothing is serialised at all — and it is round-robin rather than random,
/// because a random pick reuses the same address in bursts and that is exactly what gets an address banned.
/// </remarks>
public sealed class ProxyRotation : IProxyRotation
{
    private readonly ProxyEndpoint[] _endpoints;
    private int _cursor = -1;

    /// <summary>Parses the configured proxies, rejecting the whole set when one cannot be read.</summary>
    /// <exception cref="OptionsValidationException">A configured proxy is malformed.</exception>
    public ProxyRotation(IOptions<IdentityOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _endpoints = [.. Parsed(options.Value.Proxies)];
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public ProxyEndpoint? For(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (_endpoints.Length == 0)
            return null;

        var hash = 2166136261u;

        foreach (var character in host)
            hash = (hash ^ char.ToLowerInvariant(character)) * 16777619u;

        return _endpoints[hash % (uint)_endpoints.Length];
    }

    /// <inheritdoc />
    public ProxyEndpoint? Next()
    {
        if (_endpoints.Length == 0)
            return null;

        var next = (uint)Interlocked.Increment(ref _cursor);

        return _endpoints[next % (uint)_endpoints.Length];
    }

    private static IEnumerable<ProxyEndpoint> Parsed(IEnumerable<string> configured)
    {
        foreach (var entry in configured)
        {
            if (!ProxyEndpoint.TryParse(entry, out var endpoint))
                throw new OptionsValidationException(
                    IdentityOptions.SectionName,
                    typeof(IdentityOptions),
                    [$"'{IdentityOptions.SectionName}:{nameof(IdentityOptions.Proxies)}' holds an entry that is not '[scheme://][user:password@]host:port'."]);

            yield return endpoint;
        }
    }
}
