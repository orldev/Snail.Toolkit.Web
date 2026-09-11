namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// A page to open and the moves to make on it.
/// </summary>
/// <remarks>
/// An errand is data, not code, so a new site is a new description rather than a new release — the same
/// reasoning that put source descriptions in files elsewhere in this stack.
/// </remarks>
public sealed record Errand(Link Address, IReadOnlyList<Step> Steps);
