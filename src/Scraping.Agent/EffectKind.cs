namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// The kinds of consequence a step can be held to.
/// </summary>
public enum EffectKind
{
    /// <summary>The page asked the site something, matching a pattern in the request URL.</summary>
    Request,

    /// <summary>An element matching a selector became visible.</summary>
    Appears,

    /// <summary>An element matching a selector stopped being visible.</summary>
    Vanishes,

    /// <summary>The rendered text of the page came to contain a phrase.</summary>
    Text
}
