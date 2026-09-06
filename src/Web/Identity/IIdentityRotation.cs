namespace Snail.Toolkit.Web.Identity;

/// <summary>
/// Produces the identity a single visit is made under.
/// </summary>
public interface IIdentityRotation
{
    /// <summary>Takes the next identity, with its proxy already chosen.</summary>
    WebIdentity Next();
}
