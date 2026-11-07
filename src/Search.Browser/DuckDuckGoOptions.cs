namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// Which DuckDuckGo front end the browser asks.
/// </summary>
public sealed class DuckDuckGoOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Search:DuckDuckGoBrowser";

    /// <summary>Gets or sets the host the query is sent to, without a scheme.</summary>
    /// <remarks>
    /// The site itself rather than its HTML endpoint, because that is the whole point of reading it with a
    /// browser: the endpoint answers a browser with the same challenge it shows a plain request. A setting rather
    /// than a constant for the reason Brave's is — which front end answers is a deployment decision.
    /// </remarks>
    public string Domain { get; set; } = "duckduckgo.com";
}
