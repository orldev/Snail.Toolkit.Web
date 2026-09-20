namespace Snail.Toolkit.Web.Identity;

/// <summary>
/// Produces the identity a single visit is made under.
/// </summary>
public interface IIdentityRotation
{
    /// <summary>The identity this host always sees, drawn once and answered the same way forever after.</summary>
    /// <param name="host">The site being visited; only the name matters, and its case does not.</param>
    /// <remarks>
    /// A site that let us in remembers WHOM it let in: a cleared challenge hands back a cookie bound to the
    /// address and the user agent that solved it — Cloudflare's <c>cf_clearance</c> is the common one. Keeping
    /// that cookie while drawing the next identity from the round makes it worthless, and makes the visit
    /// stranger than one with no cookie at all: a returning visitor whose browser changed between requests.
    /// <para>
    /// The pick is a stable hash of the host rather than a remembered map, so it survives a restart and costs
    /// no memory, and the exit is picked the same way for the same reason — a clearance cookie is bound to the
    /// address that earned it.
    /// </para>
    /// </remarks>
    WebIdentity For(string host);

    /// <summary>Takes the next identity, with its proxy already chosen.</summary>
    WebIdentity Next();
}
