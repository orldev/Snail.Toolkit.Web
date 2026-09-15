using System.Diagnostics;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Reads <c>robots.txt</c> as what it is: a file written by the site being read, under no obligation to be
/// sensible, and reachable by anyone who can put a host in front of this library.
/// </summary>
/// <remarks>
/// <see cref="RobotsFile"/> already treats the file as hostile in one place — the match deadline on a pattern
/// written to backtrack — and the rest of it trusts the site completely. Every check here is driven by a file
/// a site could publish this afternoon, and each one currently ends the read rather than the line.
/// <para>
/// The distinction that matters throughout: a value this class cannot use should cost the line it was written
/// on, never the fetch. That is already the stated contract — an unparseable <c>Crawl-delay</c> answers
/// unknown, an unparseable document permits everything — and these are the values that escape it.
/// </para>
/// </remarks>
public class HostileRobotsTests
{
    /// <summary>A gap that is not a duration costs the line, not the read.</summary>
    /// <remarks>
    /// <c>double.TryParse</c> under <c>NumberStyles.Float</c> accepts far more than a count of seconds:
    /// <c>NaN</c> and <c>Infinity</c> parse by name, and any exponent past <c>1e308</c> parses as infinity.
    /// The only guard is <c>seconds &lt; 0</c>, and <c>NaN &lt; 0</c> is false — so all four reach
    /// <see cref="TimeSpan.FromSeconds"/>, which answers <c>NaN</c> with <see cref="ArgumentException"/> and
    /// the rest with <see cref="OverflowException"/>.
    /// <para>
    /// It escapes <see cref="RobotsFile.Parse"/>, which nothing above it catches: <c>HttpRobotsSource</c>
    /// filters for <c>HttpRequestException</c> and cancellation, and <c>SiteRules</c> rethrows after
    /// forgetting the entry — so the site is re-read and throws again on the next attempt, and every page of
    /// that origin fails for as long as the file stands.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e308")]
    [InlineData("1E+400")]
    public void Parse_CrawlDelayThatIsNotADuration_CostsTheLineRatherThanTheRead(string written)
    {
        var file = RobotsFile.Parse($"""
            User-agent: *
            Disallow: /private
            Crawl-delay: {written}
            """);

        Assert.Null(file.CrawlDelay("*"));
        Assert.False(file.IsAllowed("/private/x", "*"));
    }

    /// <summary>A gap longer than a timer can hold is answered, not thrown.</summary>
    /// <remarks>
    /// The gap a site asks for travels from here into <c>HostPoliteness.EnterAsync</c> and ends at
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>, which refuses anything over
    /// <c>uint.MaxValue - 1</c> milliseconds — about 49.7 days. Nothing between the two clamps it:
    /// <c>LongestRest</c> governs <c>Retry-After</c> through <c>Attempts.IsWorthRepeating</c> and never sees
    /// a <c>Crawl-delay</c> at all.
    /// <para>
    /// Measured: <c>Task.Delay(TimeSpan.FromDays(60))</c> throws <see cref="ArgumentOutOfRangeException"/>,
    /// and it is thrown out of <c>EnterAsync</c> — past the <c>catch</c> that releases the slot, so the slot
    /// survives, but the caller receives an argument exception naming a parameter it never supplied.
    /// </remarks>
    [Fact]
    public async Task EnterAsync_SiteAskingForAGapNoTimerCanHold_IsAnsweredRatherThanThrown()
    {
        // 6,000,000 seconds is sixty-nine days. A site writes it in one line.
        var asked = RobotsFile.Parse("""
            User-agent: *
            Crawl-delay: 6000000
            """).CrawlDelay("*");

        using var politeness = Paced(TimeSpan.Zero, TimeSpan.FromSeconds(10));

        var url = new Uri("https://patient.example/a");

        // The gap is measured from the end of the previous turn, and a host never spoken to has none — so
        // the first read of any site ignores whatever it asked for, and the second pays all of it.
        await using (await politeness.EnterAsync(url, asked))
        {
        }

        await using var second = await politeness.EnterAsync(url, asked);
    }

    /// <summary>A pace longer than anyone would keep is cut to the ceiling, not sat out.</summary>
    /// <remarks>
    /// A <c>Crawl-delay</c> of a day used to be waited in full, inside the gate, holding both the host's only
    /// slot and one of <c>MaxDegreeOfParallelism</c> workers for twenty-four hours. Four such sites in one
    /// list is the whole crawl stopped, by four lines of text the sites wrote themselves, and nothing in the
    /// log says so: a worker that is waiting is not failing.
    /// <para>
    /// Answered with a ceiling rather than with the refusal <c>LongestRest</c> gives <c>Retry-After</c>, and
    /// the difference is in the headers rather than in the numbers: <c>Retry-After</c> accompanies a refusal,
    /// so handing it straight back passes on something the site said, while <c>Crawl-delay</c> refuses
    /// nothing and answering it with a refusal would drop the site instead of reading it slowly.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task DelayAsync_SiteAskingForADayOfSilence_IsNotWaitedOutInFull()
    {
        var rules = new SiteRules(
            new OneFile(RobotsFile.Parse("""
                User-agent: *
                Crawl-delay: 86400
                """)),
            Options.Create(new RobotsOptions { IsObeyed = true, LongestCrawlDelay = TimeSpan.FromMinutes(1) }));

        var asked = await rules.DelayAsync(new Uri("https://glacial.example/a"), null);

        Assert.Equal(TimeSpan.FromMinutes(1), asked);
    }

    /// <summary>A pace a site asks for inside the ceiling is obeyed exactly, ceiling or no ceiling.</summary>
    /// <remarks>
    /// The other half of the same rule, and the one that keeps the cure from being worse: sites asking for
    /// thirty or sixty seconds are ordinary and mean it, and a ceiling that quietly rounded them down would
    /// make this library ruder than it was told to be while looking like it obeyed.
    /// </remarks>
    [Fact]
    public async Task DelayAsync_SiteAskingForAPaceInsideTheCeiling_IsObeyedExactly()
    {
        var rules = new SiteRules(
            new OneFile(RobotsFile.Parse("""
                User-agent: *
                Crawl-delay: 30
                """)),
            Options.Create(new RobotsOptions { IsObeyed = true, LongestCrawlDelay = TimeSpan.FromMinutes(1) }));

        var asked = await rules.DelayAsync(new Uri("https://patient.example/a"), null);

        Assert.Equal(TimeSpan.FromSeconds(30), asked);
    }

    /// <summary>The cost of deciding one address is not the site's to choose.</summary>
    /// <remarks>
    /// A per-match deadline of a hundred milliseconds used to be the whole answer, and it was the wrong shape:
    /// it bounds ONE rule while <c>IsAllowed</c> runs every rule in the group, and nothing bounds how many
    /// rules a file holds. Two hundred lines of a pattern written to backtrack bought twenty seconds per
    /// address; a hundred thousand lines — three megabytes, an ordinary body — the better part of three hours.
    /// <para>
    /// <c>RegexOptions.NonBacktracking</c> is what answers it now, and it answers completely rather than
    /// within a budget: the translated pattern is literals and <c>.*</c>, which that engine runs in time
    /// linear in the path however the pattern is written. The catastrophe is not caught, it cannot be
    /// expressed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task IsAllowed_AFileOfRulesWrittenToBacktrack_StillAnswersInReasonableTime()
    {
        var rules = string.Join('\n', Enumerable.Repeat("Disallow: /a*a*a*a*a*a*a*a*a*a*a*a*a*a*a*b", 200));
        var file = RobotsFile.Parse($"User-agent: *\n{rules}");
        var path = $"/{new string('a', 200)}c";

        // Answered on a thread of its own so that the check ends when the budget does, rather than when the
        // twenty seconds of backtracking do.
        var answering = Task.Run(() => file.IsAllowed(path, "*"));

        var finished = await Task.WhenAny(answering, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.True(
            ReferenceEquals(finished, answering),
            "one URL against one site's rules spent longer than a page is worth deciding whether to fetch it");
    }

    /// <summary>Parsing a file is not a reason to compile a regular expression for every line of it.</summary>
    /// <remarks>
    /// Rules are compiled in <c>Parse</c>, so the cost is paid by any site that answers <c>robots.txt</c>,
    /// whether or not a single URL of it is ever checked. A megabyte of <c>Disallow</c> lines is an ordinary
    /// response body and an extraordinary amount of work to do before deciding anything.
    /// </remarks>
    [Fact]
    /// <remarks>
    /// THE THRESHOLD IS IN SECONDS ON PURPOSE, and an allocation guard was written here on 2026-09-15 and
    /// THROWN AWAY, because the measurement said it would guard nothing. What <c>IsPlainPrefix</c> saves is
    /// CPU, not memory: a rule without wildcards builds no <see cref="Regex"/>, and a non-backtracking engine
    /// is expensive to CONSTRUCT rather than to hold.
    /// <para>
    /// MEASURED per thread over these same thirty thousand rules: 8.94 MB with the optimisation and 8.99 MB
    /// without it — six tenths of one percent, where the same removal moves the clock from 40 ms to 2.3 s. A
    /// check on allocation would have been green either way, which is worse than no check: it would have
    /// looked like a guard while the thing it named went unwatched.
    /// </para>
    /// <para>
    /// The lesson generalises past this file. An allocation threshold is steadier than a timing one and
    /// should be preferred WHERE THE COST IS MEMORY — it is the wrong instrument where the cost is time, and
    /// which of the two a cure saves is a question for a measurement rather than for taste.
    /// </para>
    /// </remarks>
    public void Parse_AMegabyteOfRules_DoesNotCostMoreThanReadingAPage()
    {
        var rules = string.Join('\n', Enumerable.Range(0, 30_000).Select(index => $"Disallow: /section/{index}/"));

        var started = Stopwatch.GetTimestamp();
        var file = RobotsFile.Parse($"User-agent: *\n{rules}");
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.False(file.IsAllowed("/section/17/page", "*"));
        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"parsing one robots.txt took {elapsed}");
    }

    /// <summary>The sitemaps a site names are read once each, however many times it names them.</summary>
    /// <remarks>
    /// Duplicates are dropped with <c>List.Contains</c>, which is a scan, so a file naming the same sitemap
    /// n times costs n² comparisons. Ten thousand identical <c>Sitemap</c> lines is forty kilobytes of body.
    /// </remarks>
    [Fact]
    public void Parse_ASitemapNamedTenThousandTimes_IsListedOnceAndCheaply()
    {
        var lines = string.Join('\n', Enumerable.Repeat("Sitemap: https://example.com/sitemap.xml", 10_000));

        var started = Stopwatch.GetTimestamp();
        var file = RobotsFile.Parse(lines);
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Single(file.Sitemaps);
        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"reading one list of sitemaps took {elapsed}");
    }

    /// <summary>A source that hands back one file, for the checks that are about the file and not the fetch.</summary>
    private sealed class OneFile(RobotsFile file) : IRobotsSource
    {
        public Task<RobotsFile> ReadAsync(Uri origin, ProxyEndpoint? proxy, CancellationToken ct = default)
            => Task.FromResult(file);
    }

    private static HostPoliteness Paced(TimeSpan minDelay, TimeSpan longestRest)
        => new(Options.Create(new PolitenessOptions
        {
            MinDelayPerHost = minDelay,
            MaxConcurrentPerHost = 1,
            LongestRest = longestRest
        }));
}
