namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// Which Brave front end is asked.
/// </summary>
public sealed class BraveOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Search:Brave";

    /// <summary>Gets or sets the host the query is sent to, without a scheme.</summary>
    /// <remarks>
    /// A setting rather than a constant for the same reason Google's is: the engine runs more than one front
    /// end, and which one answers is a deployment decision. Interface language and region are not settings
    /// here — they come from the identity the page is fetched under, which is the only way they cannot
    /// contradict the user agent beside them.
    /// </remarks>
    public string Domain { get; set; } = "search.brave.com";
}
