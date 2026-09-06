namespace Snail.Toolkit.Web.Text;

/// <summary>
/// Reads the addresses a document points at, and the one it claims for itself.
/// </summary>
public interface ILinkExtraction
{
    /// <summary>Resolves every <c>href</c> a robot may follow against the address the page was read from.</summary>
    IReadOnlyList<Link> From(string html, Uri address);

    /// <summary>Reads the address the document declares as its own, when it declares one.</summary>
    /// <remarks>
    /// The same content is served at several addresses on nearly every real site — a print view, a page
    /// parameter, a session id. <see cref="Link"/> reduces the forms of one address; only the page itself can
    /// say that two different addresses are one document, and this is where it says so.
    /// </remarks>
    Link? CanonicalOf(string html, Uri address);
}
