namespace Snail.Toolkit.Web.Identity;

/// <summary>
/// Which identities and proxies the toolkit rotates through.
/// </summary>
/// <remarks>
/// Bound through the standard options pattern and validated when the host starts, so a proxy that cannot be
/// read refuses to bring the process up instead of throwing from the middle of a fetch.
/// </remarks>
public sealed class IdentityOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Identity";

    /// <summary>Gets or sets the proxies to rotate through, as <c>[scheme://][user:password@]host:port</c>.</summary>
    /// <remarks>
    /// Parsed once when the host starts. Parsing used to happen on every page, inside a lock, and threw
    /// <see cref="FormatException"/> from a hot path when an entry was malformed.
    /// </remarks>
    public List<string> Proxies { get; set; } = [];

    /// <summary>Gets or sets the fingerprints to rotate through. Empty uses the built-in set.</summary>
    public List<Fingerprint> Fingerprints { get; set; } = [];
}
