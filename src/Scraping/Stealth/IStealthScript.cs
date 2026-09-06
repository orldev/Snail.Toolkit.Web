using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping.Stealth;

/// <summary>
/// Produces the script injected before any page script runs.
/// </summary>
public interface IStealthScript
{
    /// <summary>Builds the script that makes the browser agree with <paramref name="identity"/>.</summary>
    string For(WebIdentity identity);
}
