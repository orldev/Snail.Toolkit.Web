using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers the sequence every caller that drives a browser shares, and the retry that rides on it.
/// </summary>
/// <remarks>
/// The sequence was written out by hand three times and the three drifted: the page reader carried a retry from
/// the start, the site call was given one only after a measurement, and the agent had none, so a browser that
/// went away in the middle of a form lost the errand outright.
/// </remarks>
public class AttemptsTests
{
    private static readonly Link Address = Link.Of("https://example.com/a");

    [Fact]
    public async Task AtAsync_TransientFailure_IsTriedAgainUntilItAnswers()
    {
        var attempted = 0;

        var outcome = await Subject().AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(attempted < 3 ? Blocked() : Answered("ok"));
        });

        Assert.Equal(3, attempted);
        Assert.True(outcome.IsSuccess);
    }

    /// <summary>An empty page is the site's final answer, and asking for it again only spends the budget.</summary>
    [Fact]
    public async Task AtAsync_FailureTheSiteMeant_IsNotTriedAgain()
    {
        var attempted = 0;

        await Subject().AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(Failed(FailureKind.Empty));
        });

        Assert.Equal(1, attempted);
    }

    /// <summary>
    /// A fresh identity per attempt is what makes a second attempt worth making — where the site is not one we
    /// remember.
    /// </summary>
    /// <remarks>
    /// The two cannot both be had, and <see cref="ScrapingOptions.RememberSiteFor"/> decides: a kept cookie is
    /// bound to the browser that earned it, so a host whose visit is remembered keeps one identity. The memory
    /// is switched off here, which is what makes this a test of the rotation rather than of the setting.
    /// </remarks>
    [Fact]
    public async Task AtAsync_EveryAttempt_DrawsItsOwnIdentity()
    {
        var rotation = new CountingIdentities();
        var attempted = 0;

        await Subject(identities: rotation, remembers: false).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(attempted < 3 ? Blocked() : Answered("ok"));
        });

        Assert.Equal(3, rotation.Drawn);
    }

    /// <summary>
    /// A host whose visit is remembered keeps ONE identity across the attempts, because the cookie it may have
    /// left is bound to the browser that earned it.
    /// </summary>
    [Fact]
    public async Task AtAsync_SiteThatIsRemembered_KeepsOneIdentityAcrossAttempts()
    {
        var rotation = new CountingIdentities();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var attempted = 0;

        await Subject(identities: rotation).AtAsync(Address, (identity, _, _) =>
        {
            attempted++;
            seen.Add(identity.UserAgent);

            return Task.FromResult(attempted < 3 ? Blocked() : Answered("ok"));
        });

        Assert.Equal(3, attempted);
        Assert.Single(seen);
    }

    /// <summary>The ruling and the gate follow that identity rather than one left over from a failed attempt.</summary>
    [Fact]
    public async Task AtAsync_EveryAttempt_AsksTheSiteAndWaitsItsTurnAgain()
    {
        var rules = new OpenRules();
        var gate = new CountingPoliteness();
        var attempted = 0;

        await Subject(rules: rules, politeness: gate).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(attempted < 2 ? Blocked() : Answered("ok"));
        });

        Assert.Equal(2, rules.Asked);
        Assert.Equal(2, gate.Entered);
        Assert.Equal(2, gate.Left);
    }

    /// <summary>A gap the site asked for is written into the gate, where it binds every later caller.</summary>
    /// <remarks>
    /// Not into the retry delay: that would bind this operation alone, and a <c>429</c> answered to one worker
    /// is not a fact about that worker. Passed on, the retry needs nothing of its own — the next attempt enters
    /// the gate like any other and finds it closed.
    /// </remarks>
    [Fact]
    public async Task AtAsync_SiteThatAskedToBeLeftAlone_PassesTheGapToTheGate()
    {
        var gate = new CountingPoliteness();

        await Subject(politeness: gate, maxRetryAttempts: 0)
            .AtAsync(Address, (_, _, _) => Task.FromResult(Busy(TimeSpan.FromSeconds(30))));

        Assert.Equal(TimeSpan.FromSeconds(30), gate.Rested);
    }

    /// <summary>A site that asked for nothing is held to nothing beyond the configured pace.</summary>
    [Fact]
    public async Task AtAsync_FailureWithoutAGap_LeavesTheGateAlone()
    {
        var gate = new CountingPoliteness();

        await Subject(politeness: gate, maxRetryAttempts: 0)
            .AtAsync(Address, (_, _, _) => Task.FromResult(Blocked()));

        Assert.Null(gate.Rested);
    }

    [Fact]
    public async Task AtAsync_SiteThatForbidsTheUrl_NeverReachesTheWork()
    {
        var reached = false;

        var outcome = await Subject(rules: new ClosedRules()).AtAsync(Address, (_, _, _) =>
        {
            reached = true;

            return Task.FromResult(Answered("ok"));
        });

        Assert.False(reached);
        Assert.Equal(FailureKind.RobotsDisallowed, outcome.Failure?.Kind);
    }

    /// <summary>An address that is not absolute is a fact about the input, not a failure worth repeating.</summary>
    [Fact]
    public async Task AtAsync_AddressThatIsNotAbsolute_NeverReachesTheWork()
    {
        var reached = false;

        var outcome = await Subject().AtAsync(Link.Of("not a url"), (_, _, _) =>
        {
            reached = true;

            return Task.FromResult(Answered("ok"));
        });

        Assert.False(reached);
        Assert.Equal(FailureKind.Navigation, outcome.Failure?.Kind);
    }

    /// <summary>
    /// A site that asks to be left alone for longer than we are willing to wait is believed the first time.
    /// </summary>
    /// <remarks>
    /// Measured on 2026-09-14 against a fixture refusing by frequency: a gap of thirty seconds inside a budget
    /// of twenty made one request, spent the whole budget waiting in the gate and ended in a cancellation
    /// carrying neither the status nor the number — while the very first answer had already said both. The
    /// gate still holds the host for the full gap; only this caller stops waiting for it.
    /// </remarks>
    [Fact]
    public async Task AtAsync_GapLongerThanWeWillWait_MakesOneAttemptAndCarriesTheNumber()
    {
        var attempted = 0;

        var outcome = await Subject(longestRest: TimeSpan.FromSeconds(10)).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(Busy(TimeSpan.FromSeconds(30)));
        });

        Assert.Equal(1, attempted);
        Assert.Equal(TimeSpan.FromSeconds(30), outcome.Failure?.RetryAfter);
    }

    /// <summary>A gap short enough to sit out is sat out, and the work is repeated as it always was.</summary>
    /// <remarks>
    /// The pair of these two is the point. Without this one, refusing to retry everything would pass the check
    /// above just as well, and the ceiling could be anything at all.
    /// </remarks>
    [Fact]
    public async Task AtAsync_GapShorterThanWeWillWait_IsStillRepeated()
    {
        var attempted = 0;

        await Subject(longestRest: TimeSpan.FromSeconds(10)).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(Busy(TimeSpan.FromSeconds(3)));
        });

        Assert.Equal(4, attempted);
    }

    /// <summary>A refusal that names no gap is unaffected by the ceiling.</summary>
    /// <remarks>
    /// The ceiling reads a number the site sent, and most refusals send none. Reading its absence as "longer
    /// than we will wait" would quietly switch off retries for every blocked page in the toolkit.
    /// </remarks>
    [Fact]
    public async Task AtAsync_FailureWithoutAGap_IsRepeatedWhateverTheCeiling()
    {
        var attempted = 0;

        await Subject(longestRest: TimeSpan.Zero).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(Blocked());
        });

        Assert.Equal(4, attempted);
    }

    /// <summary>
    /// For a search engine a wall is not a fingerprint to be changed but a frequency to be respected: asked again
    /// five seconds later, it answers the same and remembers the second request too.
    /// </summary>
    /// <remarks>
    /// The engines measured on 2026-09-12 closed over frequency, not fingerprint. Read on 2026-09-20: every
    /// browser engine went through the same retry as a crawl, so a wall cost four requests over half a minute
    /// before the search could rest the engine — and that is most of the 136 to 392 seconds a consumer measured
    /// against a one-minute setting.
    /// </remarks>
    [Fact]
    public async Task AtAsync_ForAnEngine_AWallIsNotRepeated()
    {
        var attempted = 0;

        await Subject(repeatsRefusals: false).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(Blocked());
        });

        Assert.Equal(1, attempted);
    }

    [Fact]
    public async Task AtAsync_ForAnEngine_BusyWithoutAGap_IsNotRepeated()
    {
        var attempted = 0;

        await Subject(repeatsRefusals: false).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(Status(429));
        });

        Assert.Equal(1, attempted);
    }

    /// <summary>A gap the engine named itself is an invitation, and it is taken up as it always was.</summary>
    [Fact]
    public async Task AtAsync_ForAnEngine_AGapShortEnoughToWait_IsStillRepeated()
    {
        var attempted = 0;

        await Subject(repeatsRefusals: false).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(Busy(TimeSpan.FromSeconds(3)));
        });

        Assert.Equal(4, attempted);
    }

    /// <summary>
    /// Only refusals are spared. A browser that went away is our failure, and the engine never saw the request
    /// — without this pair, switching off every retry would pass the checks above just as well.
    /// </summary>
    [Fact]
    public async Task AtAsync_ForAnEngine_ABrowserThatWentAway_IsStillRepeated()
    {
        var attempted = 0;

        await Subject(repeatsRefusals: false).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(Failed(FailureKind.BrowserUnavailable));
        });

        Assert.Equal(4, attempted);
    }

    /// <summary>
    /// A deployment whose target closes over frequency rather than fingerprint says so in its settings, and
    /// then a wall costs one request instead of four.
    /// </summary>
    /// <remarks>
    /// MEASURED on 2026-09-20 on the defaults: a page that answers costs one request, one turn at the gate and
    /// 14 ms; a wall costs four of each and 17.7 seconds of backoff — and on a host paced at thirty seconds,
    /// four turns at the gate are two minutes for one page. On a site that refuses everything a forty-minute
    /// crawl buys some sixty-five pages instead of two hundred and sixty. The default stays as it was, because
    /// a wall is often a fingerprint and the next identity does pass it; what the setting adds is the other
    /// case, which no default can be right about.
    /// </remarks>
    [Fact]
    public async Task AtAsync_DeploymentThatDoesNotRepeatRefusals_MakesOneAttemptOnAWall()
    {
        var attempted = 0;

        await Configured(new ScrapingOptions { RepeatsRefusals = false, RetryDelay = TimeSpan.Zero })
            .AtAsync(Address, (_, _, _) =>
            {
                attempted++;

                return Task.FromResult(Blocked());
            });

        Assert.Equal(1, attempted);
    }

    /// <summary>And the default is what it always was, which is what makes the setting a choice.</summary>
    [Fact]
    public async Task AtAsync_DeploymentThatSaidNothing_StillRepeatsAWall()
    {
        var attempted = 0;

        await Configured(new ScrapingOptions { RetryDelay = TimeSpan.Zero }).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(Blocked());
        });

        Assert.Equal(4, attempted);
    }

    [Fact]
    public async Task AtAsync_RetriesSwitchedOff_MakesOneAttempt()
    {
        var attempted = 0;

        await Subject(maxRetryAttempts: 0).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(Blocked());
        });

        Assert.Equal(1, attempted);
    }

    /// <summary>The sequence a deployment gets from its settings, through the constructor DI uses.</summary>
    private static Attempts<string> Configured(ScrapingOptions options)
        => new(
            new CountingIdentities(),
            new CountingPoliteness(),
            new OpenRules(),
            Options.Create(options),
            Options.Create(new PolitenessOptions { LongestRest = TimeSpan.FromSeconds(10) }),
            NullLogger<Attempts<string>>.Instance);

    private static Attempts<string> Subject(
        IIdentityRotation? identities = null,
        IPoliteness? politeness = null,
        ISiteRules? rules = null,
        int maxRetryAttempts = 3,
        TimeSpan? longestRest = null,
        bool repeatsRefusals = true,
        bool remembers = true)
        => new(
            identities ?? new CountingIdentities(),
            politeness ?? new CountingPoliteness(),
            rules ?? new OpenRules(),
            Options.Create(new ScrapingOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                RetryDelay = TimeSpan.Zero,
                RememberSiteFor = remembers ? TimeSpan.FromHours(1) : TimeSpan.Zero
            }),
            Options.Create(new PolitenessOptions
            {
                LongestRest = longestRest ?? TimeSpan.FromSeconds(10)
            }),
            NullLogger<Attempts<string>>.Instance,
            repeatsRefusals);

    private static Outcome<string, PageFailure> Answered(string body)
        => Outcome<string, PageFailure>.Success(body);

    private static Outcome<string, PageFailure> Blocked() => Failed(FailureKind.Blocked);

    private static Outcome<string, PageFailure> Busy(TimeSpan asked)
        => Outcome<string, PageFailure>.Failed(
            new PageFailure(FailureKind.Navigation, "The site answered with HTTP 429.")
            {
                StatusCode = 429,
                RetryAfter = asked
            });

    private static Outcome<string, PageFailure> Status(int status)
        => Outcome<string, PageFailure>.Failed(
            new PageFailure(FailureKind.Navigation, $"The site answered with HTTP {status}.") { StatusCode = status });

    private static Outcome<string, PageFailure> Failed(FailureKind kind)
        => Outcome<string, PageFailure>.Failed(new PageFailure(kind, "measured") { Address = Address });

    private sealed class CountingIdentities : IIdentityRotation
    {
        public int Drawn { get; private set; }

        /// <summary>The same host always sees the same browser, as the real rotation does.</summary>
        public WebIdentity For(string host)
        {
            Drawn++;

            return Identity("Steady");
        }

        public WebIdentity Next()
        {
            Drawn++;

            return Identity($"Agent/{Drawn}");
        }

        private static WebIdentity Identity(string userAgent)
            => new()
            {
                UserAgent = userAgent,
                Platform = "Windows",
                NavigatorPlatform = "Win32",
                Brand = "Google Chrome",
                MajorVersion = "124",
                Locale = "en-US",
                Timezone = "UTC",
                AcceptLanguage = "en-US,en;q=0.9",
                Languages = ["en-US", "en"]
            };
    }

    private sealed class CountingPoliteness : IPoliteness
    {
        public DateTimeOffset OpensAt(Uri url) => DateTimeOffset.MinValue;

        public int Entered { get; private set; }

        public int Left { get; private set; }

        public TimeSpan? Rested { get; private set; }

        public Task<IAsyncDisposable> EnterAsync(Uri url, TimeSpan? requested = null, CancellationToken ct = default)
        {
            Entered++;

            return Task.FromResult<IAsyncDisposable>(new Turn(this));
        }

        public void Rest(Uri url, TimeSpan gap) => Rested = gap;

        private sealed class Turn(CountingPoliteness gate) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                gate.Left++;

                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class OpenRules : ISiteRules
    {
        public int Asked { get; private set; }

        public ValueTask<bool> IsAllowedAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
        {
            Asked++;

            return ValueTask.FromResult(true);
        }

        public ValueTask<TimeSpan?> DelayAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
            => ValueTask.FromResult<TimeSpan?>(null);

        public ValueTask<RobotsFile> RulesAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
            => ValueTask.FromResult(RobotsFile.AllowAll);
    }

    private sealed class ClosedRules : ISiteRules
    {
        public ValueTask<bool> IsAllowedAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
            => ValueTask.FromResult(false);

        public ValueTask<TimeSpan?> DelayAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
            => ValueTask.FromResult<TimeSpan?>(null);

        public ValueTask<RobotsFile> RulesAsync(Uri url, ProxyEndpoint? proxy, CancellationToken ct = default)
            => ValueTask.FromResult(RobotsFile.AllowAll);
    }
}
