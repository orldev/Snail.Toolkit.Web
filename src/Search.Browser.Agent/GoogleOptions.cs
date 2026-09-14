namespace Snail.Toolkit.Web.Search.Browser.Agent;

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

    /// <summary>Gets or sets the gap left between asking where two wrapped links lead.</summary>
    /// <remarks>
    /// Every result on a Google page is a wrapper, so one query is followed by one request per link, and those
    /// requests are not paced by <c>Web:Politeness:HostDelays</c>: the gap configured there is the one between
    /// two searches, and at the two minutes this engine is given a seven-link answer would take a quarter of an
    /// hour. Measured on 2026-09-14 from one address: six wrappers followed in turn at this interval were all
    /// answered with the address they wrap. Following them faster, or at once, was not measured — so this is the
    /// interval known to work rather than the shortest one that does.
    /// </remarks>
    public TimeSpan FollowDelay { get; set; } = TimeSpan.FromSeconds(1.5);
}
