using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Robots;

/// <summary>
/// Answers what a site's published rules say.
/// </summary>
public interface ISiteRules
{
    /// <summary>Checks the site's rules, reading <c>robots.txt</c> on first use and caching the answer.</summary>
    ValueTask<bool> IsAllowedAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default);

    /// <summary>Answers the gap the site asks to be left between fetches, when it asks for one.</summary>
    /// <remarks>
    /// Answers nothing where <c>robots.txt</c> is not obeyed, and reads no file to find out: a caller that
    /// switched the rules off did not ask for the traffic that answering would cost.
    /// </remarks>
    ValueTask<TimeSpan?> DelayAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default);

    /// <summary>Reads the whole file, for the sitemaps and the pace a caller cannot get from permission.</summary>
    /// <remarks>
    /// This answers even where <c>robots.txt</c> is not obeyed. Obedience decides whether a refusal is honoured;
    /// it says nothing about the site's own index of its pages, which is worth having either way.
    /// </remarks>
    ValueTask<RobotsFile> RulesAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default);
}
