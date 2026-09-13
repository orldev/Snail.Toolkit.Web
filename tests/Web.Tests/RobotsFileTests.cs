using Snail.Toolkit.Web.Robots;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers robots parsing and matching, which decide whether a URL is fetched at all.
/// </summary>
public class RobotsFileTests
{
    [Fact]
    public void IsAllowed_DisallowedPrefix_ForbidsThePath()
    {
        var rules = RobotsFile.Parse("""
            User-agent: *
            Disallow: /private
            """);

        Assert.False(rules.IsAllowed("/private/report", "*"));
        Assert.True(rules.IsAllowed("/public/report", "*"));
    }

    /// <summary>The longer pattern wins, which is what lets a site open one path inside a closed tree.</summary>
    [Fact]
    public void IsAllowed_AllowLongerThanDisallow_PermitsThePath()
    {
        var rules = RobotsFile.Parse("""
            User-agent: *
            Disallow: /docs/
            Allow: /docs/public/
            """);

        Assert.False(rules.IsAllowed("/docs/secret.html", "*"));
        Assert.True(rules.IsAllowed("/docs/public/guide.html", "*"));
    }

    [Fact]
    public void IsAllowed_EmptyDisallow_PermitsEverything()
    {
        var rules = RobotsFile.Parse("""
            User-agent: *
            Disallow:
            """);

        Assert.True(rules.IsAllowed("/anything", "*"));
    }

    [Fact]
    public void IsAllowed_WildcardAndAnchor_MatchAsPatterns()
    {
        var rules = RobotsFile.Parse("""
            User-agent: *
            Disallow: /*.pdf$
            """);

        Assert.False(rules.IsAllowed("/files/manual.pdf", "*"));
        Assert.True(rules.IsAllowed("/files/manual.pdf.html", "*"));
    }

    [Fact]
    public void IsAllowed_NamedAgentWithOwnGroup_UsesThatGroup()
    {
        var rules = RobotsFile.Parse("""
            User-agent: *
            Disallow: /

            User-agent: SnailBot
            Disallow: /admin
            """);

        Assert.True(rules.IsAllowed("/catalog", "SnailBot"));
        Assert.False(rules.IsAllowed("/admin/users", "SnailBot"));
        Assert.False(rules.IsAllowed("/catalog", "OtherBot"));
    }

    [Fact]
    public void IsAllowed_CommentsAndBlankLines_AreIgnored()
    {
        var rules = RobotsFile.Parse("""
            # nothing to see here

            User-agent: *   # everyone
            Disallow: /tmp  # scratch space
            """);

        Assert.False(rules.IsAllowed("/tmp/file", "*"));
        Assert.True(rules.IsAllowed("/home", "*"));
    }

    [Fact]
    public void IsAllowed_SiteWithoutRules_PermitsEverything()
        => Assert.True(RobotsFile.AllowAll.IsAllowed("/anything", "*"));

    [Fact]
    public void IsAllowed_UnparseableDocument_PermitsEverything()
        => Assert.True(RobotsFile.Parse("<html>404 not found</html>").IsAllowed("/anything", "*"));

    /// <summary>
    /// The directive that turns reading a whole domain from a crawl into a list, and the one this parser used
    /// to drop as an unknown field.
    /// </summary>
    [Fact]
    public void Sitemaps_DeclaredOutsideAnyGroup_AreRead()
    {
        var rules = RobotsFile.Parse("""
            Sitemap: https://example.com/sitemap.xml
            User-agent: *
            Disallow: /private
            Sitemap: https://example.com/news.xml
            """);

        Assert.Equal(
            ["https://example.com/sitemap.xml", "https://example.com/news.xml"],
            rules.Sitemaps.Select(sitemap => sitemap.Canonical));
    }

    [Fact]
    public void Sitemaps_TheSameOneTwice_IsListedOnce()
    {
        var rules = RobotsFile.Parse("""
            Sitemap: https://example.com/sitemap.xml
            Sitemap: https://example.com/sitemap.xml?
            """);

        Assert.Single(rules.Sitemaps);
    }

    [Fact]
    public void Sitemaps_SomethingThatIsNotAnAddress_IsDropped()
        => Assert.Empty(RobotsFile.Parse("Sitemap: /sitemap.xml").Sitemaps);

    [Fact]
    public void Sitemaps_SiteThatNamesNone_IsEmpty()
        => Assert.Empty(RobotsFile.AllowAll.Sitemaps);

    /// <summary>The pace the site asks for, which on a whole-domain read is the difference it feels.</summary>
    [Fact]
    public void CrawlDelay_StatedForTheGroup_IsRead()
    {
        var rules = RobotsFile.Parse("""
            User-agent: *
            Crawl-delay: 2.5
            Disallow: /private
            """);

        Assert.Equal(TimeSpan.FromSeconds(2.5), rules.CrawlDelay("*"));
    }

    [Fact]
    public void CrawlDelay_StatedForOneAgent_LeavesTheOthersOnTheSharedGroup()
    {
        var rules = RobotsFile.Parse("""
            User-agent: *
            Crawl-delay: 1

            User-agent: SnailBot
            Crawl-delay: 10
            """);

        Assert.Equal(TimeSpan.FromSeconds(10), rules.CrawlDelay("SnailBot"));
        Assert.Equal(TimeSpan.FromSeconds(1), rules.CrawlDelay("OtherBot"));
    }

    /// <summary>Nothing asked for is not the same fact as zero seconds, so the caller keeps its own delay.</summary>
    [Fact]
    public void CrawlDelay_SiteThatAsksForNone_AnswersUnknown()
        => Assert.Null(RobotsFile.Parse("""
            User-agent: *
            Disallow: /private
            """).CrawlDelay("*"));

    [Theory]
    [InlineData("Crawl-delay: soon")]
    [InlineData("Crawl-delay: -5")]
    public void CrawlDelay_ValueThatIsNotADelay_AnswersUnknown(string line)
        => Assert.Null(RobotsFile.Parse($"""
            User-agent: *
            {line}
            """).CrawlDelay("*"));

    /// <summary>A rule written to take forever answers in the time a rule is given, and permits the fetch.</summary>
    /// <remarks>
    /// The expression comes from a file the site being read wrote. A pattern alternating literals and
    /// wildcards backtracks catastrophically on a path that nearly matches it, so without a deadline one line
    /// of one <c>robots.txt</c> holds the thread reading that site for as long as the site likes. The
    /// deadline is a hundred milliseconds and a path match needs microseconds, so a second is a ceiling this
    /// cannot reach unless the deadline is gone. Measured on 2026-09-13: this pattern against this path
    /// answers in 105 ms with the deadline and was still running after five seconds without it.
    /// </remarks>
    [Fact]
    public void IsAllowed_PatternWrittenToBacktrack_AnswersWithinTheDeadline()
    {
        var rules = RobotsFile.Parse("""
            User-agent: *
            Disallow: /a*a*a*a*a*a*a*a*a*a*a*a*a*a*a*b
            """);

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var isAllowed = rules.IsAllowed($"/{new string('a', 200)}c", "*");

        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1));
        Assert.True(isAllowed);
    }
}
