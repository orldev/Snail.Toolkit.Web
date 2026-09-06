namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// Which Google front end is asked.
/// </summary>
public sealed class GoogleOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Search:Google";

    /// <summary>Gets or sets the host the query is sent to, without a scheme.</summary>
    /// <remarks>
    /// A country front end answers differently from the international one, so this is a deployment decision
    /// rather than a constant. Interface language and region are not settings here: they come from the identity
    /// the page is fetched under, which is the only way they cannot contradict the user agent beside them.
    /// </remarks>
    public string Domain { get; set; } = "www.google.com";
}
