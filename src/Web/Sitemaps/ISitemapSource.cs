using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Sitemaps;

/// <summary>
/// Downloads one sitemap document.
/// </summary>
/// <remarks>
/// Declared here and implemented by whichever package owns a transport, for the same reason robots rules are:
/// the core describes what it needs and stays free of an HTTP client. A sitemap is also the one document that
/// must not be read with a browser — a rendered XML file comes back as the browser's own tree view, markup and
/// all, and parses as nothing.
/// </remarks>
public interface ISitemapSource
{
    /// <summary>Reads the document at <paramref name="address"/>, answering an empty one when it cannot.</summary>
    Task<SitemapFile> ReadAsync(Uri address, ProxyEndpoint? proxy, CancellationToken ct = default);
}
