namespace Snail.Toolkit.Web.Politeness;

/// <summary>
/// How hard a single site may be pressed.
/// </summary>
/// <remarks>
/// Considerable effort used to go into fingerprints and none into request rate, which is the signal a site
/// notices first and the one no amount of canvas noise hides. Three parallel workers hitting one host from one
/// address is what gets an address banned; these limits are applied per host, so a crawl spread across many
/// sites keeps its throughput while no single site sees a burst.
/// </remarks>
public sealed class PolitenessOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Politeness";

    /// <summary>Gets or sets the smallest gap between two requests to the same host.</summary>
    public TimeSpan MinDelayPerHost { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets how many requests may be in flight against one host.</summary>
    public int MaxConcurrentPerHost { get; set; } = 1;

    /// <summary>Gets or sets the longest rest a site may ask for and still be waited out.</summary>
    /// <remarks>
    /// A site that answers <c>429</c> with <c>Retry-After</c> has given a final answer for the next several
    /// seconds, and a caller told to come back later than this is better served by hearing so at once than by
    /// spending its whole budget waiting to be refused again. Measured on 2026-09-14 against a fixture that
    /// refuses by frequency: a gap of three seconds was retried three times and answered in nine, inside the
    /// default search budget of thirty; a gap of thirty seconds, inside a budget of twenty, made one request,
    /// sat out the entire budget and ended in a cancellation that named neither the status nor the gap. Ten
    /// seconds keeps the first family retried and cuts off the second, which provably cannot fit.
    /// <para>
    /// It never shortens the rest itself — the gate holds the host for as long as the site asked, whatever this
    /// says. What it decides is only whether this caller waits for it or is told now.
    /// </para>
    /// </remarks>
    public TimeSpan LongestRest { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets how long a host is remembered after the last request to it.</summary>
    /// <remarks>
    /// The gate keeps one entry per host and nothing used to remove one, so a crawl across many sites grew it
    /// for as long as the process ran. What is worth remembering is exactly what a pacing decision can still
    /// depend on: a host nobody has spoken to for longer than this gets the same answer from a fresh entry as
    /// from the kept one. Ten minutes is far longer than any gap this class hands out and short enough that a
    /// long crawl holds only the sites it is actually working on.
    /// <para>
    /// It never shortens a rest. A host still being held back, or with a request in flight, is not forgotten
    /// whatever this says.
    /// </para>
    /// </remarks>
    public TimeSpan ForgetHostAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets or sets the hosts that are pressed more gently than the rest, each with its own smallest gap.</summary>
    /// <remarks>
    /// One gap for every host is the wrong shape for a client that talks to a search engine and a regional
    /// newspaper in the same minute: the engines measured on 2026-09-12 closed over frequency, one of them after
    /// three answers in an evening, while a site with a sitemap does not mind a page a second. A key matches the
    /// host itself and every host beneath it, so <c>example.com</c> covers <c>www.example.com</c> too, and the
    /// most specific key wins. A site's own <c>Crawl-delay</c> still applies on top — both are floors.
    /// </remarks>
    public Dictionary<string, TimeSpan> HostDelays { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The gap named for this host by its own key or by the nearest one above it, when one is named at all.</summary>
    /// <remarks>
    /// The walk is the rule <see cref="HostDelays"/> states, written once rather than in each caller: the host
    /// is tried whole and then shortened one label at a time, so <c>news.example.com</c> looks for
    /// <c>news.example.com</c>, then <c>example.com</c>, then <c>com</c>. A name that merely ends the same way
    /// is not a parent, because the cut is always at a dot.
    /// <para>
    /// Nothing rather than <see cref="MinDelayPerHost"/> when no key covers the host, because that is the
    /// question a second caller had to ask: a package pacing its own engine has to tell "nobody named a gap
    /// here" from "the deployment named exactly the default", and a floor cannot say which it is. Measured on
    /// 2026-09-15 against the first consumer of the Google package: a deployment naming <c>google.com</c> was
    /// read as naming nothing, because the check there was an exact-key lookup for <c>www.google.com</c>, and
    /// both keys ended up standing with the more specific one winning — so the deployment's setting was
    /// overridden by a package that had decided it was absent.
    /// </para>
    /// <para>
    /// Case is compared here rather than left to the dictionary's own comparer: the default instance ignores
    /// it, and an instance arriving from configuration binding is not promised to.
    /// </para>
    /// </remarks>
    public TimeSpan? GapFor(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var candidate = host;

        while (true)
        {
            foreach (var named in HostDelays)
            {
                if (string.Equals(named.Key, candidate, StringComparison.OrdinalIgnoreCase))
                    return named.Value;
            }

            var dot = candidate.IndexOf('.');

            if (dot < 0)
                return null;

            candidate = candidate[(dot + 1)..];
        }
    }
}
