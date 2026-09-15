using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// A page to open and the moves to make on it.
/// </summary>
/// <remarks>
/// An errand is data, not code, so a new site is a new description rather than a new release — the same
/// reasoning that put source descriptions in files elsewhere in this stack.
/// </remarks>
public sealed record Errand(Link Address, IReadOnlyList<Step> Steps)
{
    /// <summary>Gets how much of the page the browser may fetch while the errand is walked.</summary>
    /// <remarks>
    /// Everything by default, and the default is the interesting half: a page that is DRIVEN rather than read
    /// needs the scripts that build what is driven — Google's query is typed into a field that does not exist
    /// without them. The reader's default is the same for the same reason, and both are a choice rather than
    /// an economy: measured against Brave on 2026-09-14, one ordinary page load cost 71 to 73 requests and was
    /// answered <c>429</c> three times out of three, while the document alone cost one and was answered
    /// <c>200</c> three times out of three.
    /// <para>
    /// An errand that only waits for something already in the markup — a registry page whose table is served
    /// with it, a site walked to read rather than to use — asks for the document and pays one request. Until
    /// 2026-09-15 it had no way to say so: the agent opened every visit at <see cref="Appetite.Everything"/>
    /// whatever the errand was for, so the choice existed for the reader and not here.
    /// </para>
    /// </remarks>
    public Appetite Appetite { get; init; } = Appetite.Everything;
}
