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

    /// <summary>Gets the address as it was written.</summary>
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
    /// </remarks>
    public static Link Of(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var text = url.Trim();

        if (!Uri.TryCreate(text, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            return new Link(text, text, null);

        var builder = new UriBuilder(address)
        {
            Scheme = Uri.UriSchemeHttps,
            Host = address.Host.ToLowerInvariant(),
            Fragment = string.Empty,
            Query = WithoutTracking(address.Query),
            Port = address.IsDefaultPort ? -1 : address.Port
        };

        if (builder.Path.Length > 1)
            builder.Path = builder.Path.TrimEnd('/');

        return new Link(builder.Uri.AbsoluteUri, text, address);
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
