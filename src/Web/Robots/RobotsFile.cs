using System.Globalization;
using System.Text.RegularExpressions;

namespace Snail.Toolkit.Web.Robots;

/// <summary>
/// The rules one site publishes in <c>robots.txt</c>.
/// </summary>
/// <remarks>
/// Parsing and matching are kept free of I/O so both are directly testable — the deterministic half of this
/// behaviour previously had no tests at all. Matching follows the usual precedence: the longest pattern that
/// matches wins, and <c>Allow</c> beats <c>Disallow</c> at equal length.
/// <para>
/// The file answers three questions, not one. Permission is the famous one, but <c>Sitemap</c> is how a site
/// offers its own index of pages — the difference between reading a domain by following links and being handed
/// the list — and <c>Crawl-delay</c> is the pace it asks for. Both were parsed away as unknown fields.
/// </para>
/// </remarks>
public sealed class RobotsFile
{
    private readonly Dictionary<string, List<Rule>> _groups;
    private readonly Dictionary<string, TimeSpan> _delays;

    private RobotsFile(
        Dictionary<string, List<Rule>> groups,
        Dictionary<string, TimeSpan> delays,
        IReadOnlyList<Link> sitemaps)
    {
        _groups = groups;
        _delays = delays;
        Sitemaps = sitemaps;
    }

    /// <summary>Rules that permit everything, used when a site publishes none.</summary>
    public static RobotsFile AllowAll { get; } = new([], [], []);

    /// <summary>Gets the sitemaps the site points at, in the order it named them.</summary>
    public IReadOnlyList<Link> Sitemaps { get; }

    /// <summary>Reads a <c>robots.txt</c> document.</summary>
    public static RobotsFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var groups = new Dictionary<string, List<Rule>>(StringComparer.OrdinalIgnoreCase);
        var delays = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        var sitemaps = new List<Link>();
        var agents = new List<string>();
        var isCollectingAgents = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = Strip(raw);

            if (line.Length == 0)
                continue;

            var separator = line.IndexOf(':', StringComparison.Ordinal);

            if (separator <= 0)
                continue;

            var field = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (field.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (!isCollectingAgents)
                {
                    agents.Clear();
                    isCollectingAgents = true;
                }

                agents.Add(value);
                continue;
            }

            if (field.Equals("sitemap", StringComparison.OrdinalIgnoreCase))
            {
                var sitemap = Link.Of(value);

                if (sitemap.IsAbsolute && !sitemaps.Contains(sitemap))
                    sitemaps.Add(sitemap);

                continue;
            }

            if (field.Equals("crawl-delay", StringComparison.OrdinalIgnoreCase))
            {
                isCollectingAgents = false;

                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    || seconds < 0)
                    continue;

                foreach (var agent in agents)
                    delays[agent] = TimeSpan.FromSeconds(seconds);

                continue;
            }

            var isAllow = field.Equals("allow", StringComparison.OrdinalIgnoreCase);

            if (!isAllow && !field.Equals("disallow", StringComparison.OrdinalIgnoreCase))
                continue;

            isCollectingAgents = false;

            foreach (var agent in agents)
            {
                if (!groups.TryGetValue(agent, out var rules))
                    groups[agent] = rules = [];

                if (value.Length > 0)
                    rules.Add(new Rule(isAllow, value));
            }
        }

        return new RobotsFile(groups, delays, sitemaps);
    }

    /// <summary>Answers whether <paramref name="path"/> may be fetched by <paramref name="agent"/>.</summary>
    public bool IsAllowed(string path, string agent)
    {
        var rules = Group(agent);

        if (rules is null || rules.Count == 0)
            return true;

        Rule? best = null;

        foreach (var rule in rules)
        {
            if (!rule.Matches(path))
                continue;

            if (best is null || rule.Pattern.Length > best.Pattern.Length ||
                (rule.Pattern.Length == best.Pattern.Length && rule.IsAllow))
                best = rule;
        }

        return best is null || best.IsAllow;
    }

    /// <summary>Answers how long the site asks <paramref name="agent"/> to wait between fetches.</summary>
    /// <remarks>
    /// Null means the site asked for nothing, which is not the same as asking for zero: the caller keeps its
    /// own configured delay rather than being told there is none.
    /// </remarks>
    public TimeSpan? CrawlDelay(string agent)
    {
        if (_delays.TryGetValue(agent, out var exact))
            return exact;

        return _delays.TryGetValue("*", out var any) ? any : null;
    }

    private static string Strip(string line)
    {
        var comment = line.IndexOf('#', StringComparison.Ordinal);

        return (comment < 0 ? line : line[..comment]).Trim();
    }

    private List<Rule>? Group(string agent)
    {
        if (_groups.TryGetValue(agent, out var exact))
            return exact;

        return _groups.GetValueOrDefault("*");
    }

    private sealed class Rule
    {
        private readonly Regex _matcher;

        public Rule(bool isAllow, string pattern)
        {
            IsAllow = isAllow;
            Pattern = pattern;
            _matcher = new Regex(Translate(pattern), RegexOptions.CultureInvariant);
        }

        public bool IsAllow { get; }

        public string Pattern { get; }

        public bool Matches(string path) => _matcher.IsMatch(path);

        private static string Translate(string pattern)
        {
            var isAnchored = pattern.EndsWith('$');
            var body = isAnchored ? pattern[..^1] : pattern;
            var translated = string.Join(".*", body.Split('*').Select(Regex.Escape));

            return isAnchored ? $"^{translated}$" : $"^{translated}";
        }
    }
}
