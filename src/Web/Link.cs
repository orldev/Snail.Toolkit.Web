using System.Globalization;
using System.Text;

namespace Snail.Toolkit.Web;

/// <summary>
/// One address, reduced to the form both engines agree on.
/// </summary>
/// <remarks>
/// This is the seam between search and scraping rather than a convenience. Search deduplicates results by
/// address and scraping applies politeness and robots rules by address; the moment the two normalise
/// differently, the same page counts as one link on one side and two on the other. The same page arrives as
/// http and https, with and without a trailing slash, and carrying whichever campaign parameters the
/// referrer attached, so equality is decided on <see cref="Canonical"/> alone.
/// </remarks>
public sealed record Link
{
    private static readonly string[] Tracking =
    [
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "gclid", "fbclid", "yclid", "msclkid", "mc_eid", "igshid"
    ];

    private Link(string canonical, string text, Uri? address)
    {
        Canonical = canonical;
        Text = text;
        Address = address;
    }

    /// <summary>Gets the form two providers would agree on.</summary>
    public string Canonical { get; }

    /// <summary>Gets the address as it was written, less any credentials it carried.</summary>
    public string Text { get; }

    /// <summary>Gets the parsed address, or <see langword="null"/> when the text is not an absolute http address.</summary>
    public Uri? Address { get; }

    /// <summary>Gets whether the text parsed as an absolute http or https address.</summary>
    public bool IsAbsolute => Address is not null;

    /// <summary>Reads an address into its comparable form.</summary>
    /// <remarks>
    /// Anything that is not an absolute http or https address is kept verbatim rather than rejected. A
    /// provider that hands back a relative or malformed href is still reporting something, and dropping it
    /// here would lose the fact silently — <see cref="IsAbsolute"/> is where the caller answers it.
    /// <para>
    /// Credentials survive into <see cref="Address"/> and into nothing else. Every other form of a link is
    /// read by somebody: <see cref="Canonical"/> is the key a crawl deduplicates by and the value the reader
    /// puts on the <c>url.full</c> span tag, and <see cref="Text"/> is what a log line prints. Until
    /// 2026-09-13 a password written into an address reached all three, so one link with basic credentials
    /// published them to the tracing backend and to every log that named the link.
    /// </para>
    /// </remarks>
    public static Link Of(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var text = url.Trim();

        if (!Uri.TryCreate(text, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            return new Link(text, text, null);

        return Reduced(address, text);
    }

    /// <summary>Reads an address already parsed as an absolute http or https one into its comparable form.</summary>
    /// <remarks>
    /// The same link <see cref="Of(string)"/> answers for the address's <see cref="Uri.AbsoluteUri"/>, without
    /// parsing that text a second time: a page's links are resolved against its address, and each resolution
    /// already is the parse. The parsed address is kept only where it was written exactly as the text reads, since
    /// <see cref="Address"/> is public and a resolved <c>https://example.com</c> remembers being written without
    /// its slash where the text parsed again does not; any other address goes the long way, as do credentials,
    /// whose shown form is rebuilt from the text.
    /// </remarks>
    internal static Link Of(Uri address)
    {
        var text = address.AbsoluteUri;

        return address.UserInfo.Length == 0 && string.Equals(address.OriginalString, text, StringComparison.Ordinal)
            ? Reduced(address, text)
            : Of(text);
    }

    /// <summary>Takes the comparable form from the parsed address's own parts, or builds it where that is not sure.</summary>
    /// <remarks>
    /// Building it with a <see cref="UriBuilder"/> and reading the built address back parsed every link twice and
    /// split its query into an array, a sequence and a joined string, for every link of every page, every sitemap
    /// entry and every search result: measured on 2026-10-12 by the audit, 0.47 of the time and 52 % of the bytes
    /// once the parts are concatenated instead. Concatenating is only sure where the second parse could not have
    /// changed anything, so whatever it could change — credentials, a host outside lower-case ASCII or written as
    /// IPv6, http on the port of https, a query opening with a second question mark, a path that trims down to
    /// nothing or to a dot — is still built the long way, and both ways answer the same form.
    /// </remarks>
    private static Link Reduced(Uri address, string text)
    {
        if (address.UserInfo.Length == 0 && Concatenated(address) is { } canonical)
            return new Link(canonical, text, address);

        return Built(address, text);
    }

    private static string? Concatenated(Uri address)
    {
        if (address.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4) || !IsPlainHost(address.Host))
            return null;

        if (address.Scheme == Uri.UriSchemeHttp && !address.IsDefaultPort && address.Port == 443)
            return null;

        if (Trimmed(address.AbsolutePath) is not { } path || Kept(address.Query) is not { } query)
            return null;

        return address.IsDefaultPort
            ? $"https://{address.Host}{path}{query}"
            : $"https://{address.Host}:{address.Port.ToString(CultureInfo.InvariantCulture)}{path}{query}";
    }

    private static bool IsPlainHost(string host)
    {
        foreach (var character in host)
        {
            if (!char.IsAscii(character) || char.IsUpper(character))
                return false;
        }

        return true;
    }

    private static string? Trimmed(string path)
    {
        if (path.Length <= 1 || path[^1] != '/')
            return path;

        var trimmed = path.TrimEnd('/');

        return trimmed.Length == 0 || trimmed[^1] == '.' ? null : trimmed;
    }

    /// <summary>The query without its tracking and empty pairs, the question mark included while anything is left.</summary>
    private static string? Kept(string query)
    {
        if (query.Length <= 1)
            return string.Empty;

        if (query[1] == '?')
            return null;

        var pairs = query.AsSpan(1);

        if (!HasNoise(pairs))
            return query;

        var kept = new StringBuilder(query.Length);

        foreach (var range in pairs.Split('&'))
        {
            var pair = pairs[range];

            if (pair.Length == 0 || IsTracking(pair))
                continue;

            kept.Append(kept.Length == 0 ? '?' : '&').Append(pair);
        }

        return kept.ToString();
    }

    private static bool HasNoise(ReadOnlySpan<char> pairs)
    {
        foreach (var range in pairs.Split('&'))
        {
            var pair = pairs[range];

            if (pair.Length == 0 || IsTracking(pair))
                return true;
        }

        return false;
    }

    private static bool IsTracking(ReadOnlySpan<char> pair)
    {
        var equals = pair.IndexOf('=');
        var name = equals < 0 ? pair : pair[..equals];

        foreach (var tracking in Tracking)
        {
            if (name.Equals(tracking, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Link Built(Uri address, string text)
    {
        var builder = new UriBuilder(address)
        {
            Scheme = Uri.UriSchemeHttps,
            Host = address.Host.ToLowerInvariant(),
            UserName = string.Empty,
            Password = string.Empty,
            Fragment = string.Empty,
            Query = WithoutTracking(address.Query),
            Port = address.IsDefaultPort ? -1 : address.Port
        };

        if (builder.Path.Length > 1)
            builder.Path = builder.Path.TrimEnd('/');

        return new Link(builder.Uri.AbsoluteUri, Shown(address, text), address);
    }

    /// <summary>The address in the form it is safe to print: as written, unless it was written with credentials.</summary>
    private static string Shown(Uri address, string text)
    {
        if (address.UserInfo.Length == 0)
            return text;

        var plain = new UriBuilder(address) { UserName = string.Empty, Password = string.Empty };

        return plain.Uri.AbsoluteUri;
    }

    private static string WithoutTracking(string query)
    {
        if (query.Length <= 1)
            return string.Empty;

        var kept = query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !IsTracking(pair));

        return string.Join('&', kept);
    }

    private static bool IsTracking(string pair)
    {
        var name = pair.Split('=', 2)[0];

        return Tracking.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool Equals(Link? other)
        => other is not null && string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => Canonical.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Text;
}
