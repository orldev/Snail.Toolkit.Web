namespace Snail.Toolkit.Web.Robots;

/// <summary>
/// Whether a site's published rules are obeyed, and how they are kept.
/// </summary>
public sealed class RobotsOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Web:Robots";

    /// <summary>Gets or sets whether <c>robots.txt</c> is fetched and obeyed.</summary>
    /// <remarks>
    /// Off by default only because switching it on silently would drop URLs that existing callers expect to be
    /// fetched, and a dropped URL looks exactly like a bug. Switching it on is the correct choice for any
    /// crawl of sites you do not own.
    /// </remarks>
    public bool IsObeyed { get; set; }

    /// <summary>Gets or sets how long a fetched <c>robots.txt</c> stays authoritative.</summary>
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Gets or sets the product token matched against <c>User-agent</c> groups.</summary>
    /// <remarks>
    /// A browser user agent is not a robots product token, and matching one against these groups would find
    /// nothing, so the wildcard group is the honest default.
    /// </remarks>
    public string UserAgent { get; set; } = "*";

    /// <summary>Gets or sets how long a <c>robots.txt</c> fetch may take before it is abandoned.</summary>
    /// <remarks>Applied by whichever transport implements <see cref="IRobotsSource"/>, since only it does I/O.</remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
