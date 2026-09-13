using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Scraping;

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

    /// <summary>A fresh identity per attempt is what makes a second attempt worth making.</summary>
    [Fact]
    public async Task AtAsync_EveryAttempt_DrawsItsOwnIdentity()
    {
        var rotation = new CountingIdentities();
        var attempted = 0;

        await Subject(identities: rotation).AtAsync(Address, (_, _, _) =>
        {
            attempted++;

            return Task.FromResult(attempted < 3 ? Blocked() : Answered("ok"));
        });

        Assert.Equal(3, rotation.Drawn);
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

    private static Attempts<string> Subject(
        IIdentityRotation? identities = null,
        IPoliteness? politeness = null,
        ISiteRules? rules = null,
        int maxRetryAttempts = 3)
        => new(
            identities ?? new CountingIdentities(),
            politeness ?? new CountingPoliteness(),
            rules ?? new OpenRules(),
            Options.Create(new ScrapingOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                RetryDelay = TimeSpan.Zero
            }),
            NullLogger<Attempts<string>>.Instance);

    private static Outcome<string, PageFailure> Answered(string body)
        => Outcome<string, PageFailure>.Success(body);

    private static Outcome<string, PageFailure> Blocked() => Failed(FailureKind.Blocked);

    private static Outcome<string, PageFailure> Failed(FailureKind kind)
        => Outcome<string, PageFailure>.Failed(new PageFailure(kind, "measured") { Address = Address });

    private sealed class CountingIdentities : IIdentityRotation
    {
        public int Drawn { get; private set; }

        public WebIdentity Next()
        {
            Drawn++;

            return new WebIdentity
            {
                UserAgent = $"Agent/{Drawn}",
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
    }

    private sealed class CountingPoliteness : IPoliteness
    {
        public int Entered { get; private set; }

        public int Left { get; private set; }

        public Task<IAsyncDisposable> EnterAsync(Uri url, TimeSpan? requested = null, CancellationToken ct = default)
        {
            Entered++;

            return Task.FromResult<IAsyncDisposable>(new Turn(this));
        }

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
