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
    /// <summary>The largest <c>Crawl-delay</c> read as a number rather than as nonsense.</summary>
    /// <remarks>
    /// A day. Not a policy on how long to wait — that is
    /// <see cref="Politeness.PolitenessOptions"/>'s and the caller's — only the point past which the figure
    /// stops being a pace a site is asking for. <see cref="TimeSpan"/> reaches ten million days and
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> refuses anything past about fifty, so a file
    /// asking for a year used to travel intact from here to a timer that threw on it.
    /// </remarks>
    private const double MostSecondsAsked = 24 * 60 * 60;

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
    /// <remarks>
    /// A <c>crawl-delay</c> is checked with IsFinite as well as its sign, and the sign alone is not close.
    /// NumberStyles.Float parses "NaN" and "Infinity" by name and reads any exponent past 1e308 as infinity, and
    /// NaN &lt; 0 is false — so all three reached
    /// TimeSpan.FromSeconds, which answers NaN with ArgumentException and the rest with OverflowException, out of a
    /// method whose whole contract is that a site's own file is input rather than a contract. One line of one
    /// robots.txt ended every read of that origin.
    /// </remarks>
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
                    || !double.IsFinite(seconds) || seconds < 0 || seconds > MostSecondsAsked)
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

    /// <summary>One <c>Allow</c> or <c>Disallow</c> line, matched the way the site wrote it.</summary>
    /// <remarks>
    /// The expression is built from a file the site being read wrote, so it is hostile input. A pattern of
    /// alternating literals and wildcards — <c>/a*b*c*d*e*</c> becomes <c>^/a.*b.*c.*d.*e.*</c> — backtracks
    /// catastrophically on a path that nearly matches it.
    /// <para>
    /// That used to be answered with a hundred-millisecond deadline per match, and the deadline was the wrong
    /// shape for the problem. It bounds ONE rule, while <see cref="IsAllowed"/> runs every rule in the group
    /// and nothing bounds how many rules a file may hold: MEASURED on 2026-09-15, two hundred lines of the
    /// pattern above cost twenty seconds to decide ONE address, and a file of a hundred thousand — three
    /// megabytes, an ordinary response body — would cost the better part of three hours. Multiplying a bound
    /// by an unbounded count is not a bound.
    /// </para>
    /// <para>
    /// <see cref="RegexOptions.NonBacktracking"/> answers it instead, and answers it completely:
    /// <see cref="Translate"/> emits nothing but escaped literals and <c>.*</c>, which that engine supports
    /// in full, and it matches in time linear in the length of the path whatever the pattern looks like. The
    /// catastrophe is not caught any more, it cannot be written. The deadline is gone with it, and so is the
    /// rule that ran out of time counting as no match.
    /// </para>
    /// </remarks>
    private sealed class Rule
    {
        /// <summary>The expression this rule matches by, or nothing when it needs no expression at all.</summary>
        /// <remarks>
        /// Most of a real <c>robots.txt</c> is literal prefixes — <c>/admin/</c>, <c>/cgi-bin/</c> — and a
        /// prefix is what <see cref="Translate"/> turns into <c>^</c> followed by an escaped literal, which
        /// is <see cref="string.StartsWith(string, StringComparison)"/> written the long way round. Deciding
        /// that here rather than building an expression to discover it matters because the expression is
        /// built at parse time, once per rule per named agent, for every site that answers <c>robots.txt</c>
        /// — whether or not a single address of it is ever checked.
        /// <para>
        /// MEASURED on 2026-09-15: thirty thousand literal rules cost 2.3 s to parse as non-backtracking
        /// expressions and 40 ms as prefixes. Non-backtracking is the slower engine to construct and the only
        /// safe one to run, so the answer is to construct it only where a wildcard means something has to be.
        /// </para>
        /// </remarks>
        private readonly Regex? _matcher;

        public Rule(bool isAllow, string pattern)
        {
            IsAllow = isAllow;
            Pattern = pattern;

            if (!IsPlainPrefix(pattern))
                _matcher = new Regex(
                    Translate(pattern),
                    RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        }

        public bool IsAllow { get; }

        public string Pattern { get; }

        public bool Matches(string path)
            => _matcher is null
                ? path.StartsWith(Pattern, StringComparison.Ordinal)
                : _matcher.IsMatch(path);

        /// <summary>Whether this pattern says nothing more than "the path starts with this".</summary>
        /// <remarks>
        /// A <c>$</c> anywhere but the end is an ordinary character in a path and is escaped as one, so only
        /// a trailing one makes the pattern an anchor rather than a prefix.
        /// </remarks>
        private static bool IsPlainPrefix(string pattern)
            => !pattern.Contains('*', StringComparison.Ordinal) && !pattern.EndsWith('$');

        private static string Translate(string pattern)
        {
            var isAnchored = pattern.EndsWith('$');
            var body = isAnchored ? pattern[..^1] : pattern;
            var translated = string.Join(".*", body.Split('*').Select(Regex.Escape));

            return isAnchored ? $"^{translated}$" : $"^{translated}";
        }
    }
}
