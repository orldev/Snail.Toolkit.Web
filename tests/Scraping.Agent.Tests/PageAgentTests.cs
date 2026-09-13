using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Scraping.Agent.Extensions;
using Snail.Toolkit.Web.Scraping.Behavior;
using Xunit.Abstractions;

namespace Snail.Toolkit.Web.Scraping.Agent.Tests;

/// <summary>
/// Drives the real agent against a real browser and a form that only answers someone who typed.
/// </summary>
/// <remarks>Requires Docker. Filter with <c>--filter Category!=Integration</c> to skip.</remarks>
[Collection(FormCollection.Name)]
[Trait("Category", "Integration")]
public class PageAgentTests(FormFixture site, ITestOutputHelper output)
{
    /// <summary>
    /// The whole errand: type into the field, choose what it offered, press the button, read the answer.
    /// </summary>
    /// <remarks>
    /// The form answers "Nothing found" to anyone who reached the button without choosing a suggestion, and its
    /// suggestions only appear on a key event. So this test fails for an agent that assigns the field's value
    /// instead of typing into it — which is exactly the defect measured on a live registry, where filling the
    /// field left the site silent and the walkthrough looked like an honest empty result.
    /// </remarks>
    [Fact]
    public async Task PerformAsync_TypePickAndSubmit_ReadsTheAnswerTheFormGives()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/form.html"),
        [
            new Step(StepKind.Type, Spot.Css("#party"))
            {
                Value = "Acme",
                Expected = new Effect(EffectKind.Request, "suggest.json")
            },
            new Step(StepKind.Pick, Spot.Css("#hints li"))
            {
                Value = "Acme Holdings",
                Expected = new Effect(EffectKind.Vanishes, "#hints")
            },
            new Step(StepKind.Click, Spot.Css("#find"))
            {
                Expected = new Effect(EffectKind.Text, "Case A40-1/2026")
            }
        ]));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("Case A40-1/2026 for Acme Holdings", outcome.Value.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A field named only by a caption is reached by that caption.
    /// </summary>
    /// <remarks>
    /// Measured on a live registry on 2026-09-12: its caption is a <c>label</c> carrying no <c>for</c> and no
    /// field inside it, so Playwright's own label engine matched nothing while the first field following the
    /// caption matched exactly one. Had the obvious implementation shipped, addressing by caption would have
    /// failed on the very page that asked for it. The second row is captioned by a plain <c>div</c>, which no
    /// label engine resolves at all, so the fallback is doing the work here rather than being carried.
    /// </remarks>
    [Fact]
    public async Task PerformAsync_FieldsNamedOnlyByACaption_AreReachedByThatCaption()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/captioned.html"),
        [
            new Step(StepKind.Type, Spot.Labelled("Notice number")) { Value = "2024-001" },
            new Step(StepKind.Type, Spot.Labelled("Vehicle code")) { Value = "WDC166" },
            new Step(StepKind.Click, Spot.Css("#look"))
            {
                Expected = new Effect(EffectKind.Text, "covers")
            }
        ]));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("Notice 2024-001 covers WDC166", outcome.Value.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A step the site never reacted to is named, rather than reported as a page with nothing on it.
    /// </summary>
    /// <remarks>
    /// This is the failure the package exists for. The page loads, every locator resolves, the click lands, and
    /// the result is still worthless — and an agent that only reports whether it executed its moves cannot tell
    /// that apart from a search that genuinely found nothing. The silent request log is what says which of the
    /// two it was: the site was asked nothing at all, so it never learned of the move.
    /// </remarks>
    [Fact]
    public async Task PerformAsync_StepTheSiteIgnores_NamesTheStepInsteadOfReturningThePage()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/form.html"),
        [
            new Step(StepKind.Click, Spot.Css("#find"))
            {
                Attempts = 1,
                Expected = new Effect(EffectKind.Text, "Case A40-1/2026")
                {
                    Within = TimeSpan.FromSeconds(2)
                }
            }
        ]));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Empty, outcome.Failure.Kind);
        Assert.Contains("Step 1 of 1", outcome.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("Case A40-1/2026", outcome.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("did not react", outcome.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("asked the site for nothing", outcome.Failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An address that matches nothing says so, and says what the page was offering instead.
    /// </summary>
    /// <remarks>
    /// Told apart from a site that ignored the move because the two ask for opposite repairs: a wrong address
    /// or a page that renders differently between loads against a widget that wants a real event. The fields
    /// the page does offer are named for the same reason they were gathered by hand five times on 2026-09-12 —
    /// the next question after "it matched nothing" is always "so what was there?".
    /// </remarks>
    [Fact]
    public async Task PerformAsync_AddressThatMatchesNothing_SaysSoAndNamesWhatThePageOffers()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/form.html"),
        [
            new Step(StepKind.Type, Spot.Css("#nowhere"))
            {
                Value = "Acme",
                Ready = TimeSpan.FromSeconds(2)
            }
        ]));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Empty, outcome.Failure.Kind);
        Assert.Contains("never showed", outcome.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("nothing on the page matched that address", outcome.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("party", outcome.Failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A walkthrough that ends on a challenge is a refusal, however well its moves went.
    /// </summary>
    /// <remarks>
    /// Measured against a live search engine on 2026-09-12: it accepted the typing, accepted the submit, began
    /// loading the results and then redirected to a "prove you are not a robot" page. Every step had reacted,
    /// so the agent called the errand done and handed back two thousand characters of refusal as the answer.
    /// Checking the landing page alone is not enough — a site can show the door in response to the very move
    /// the errand was written to make.
    /// </remarks>
    [Fact]
    public async Task PerformAsync_AWalkthroughEndingOnAChallenge_ReportsBlockedRatherThanTheRefusalText()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/guarded.html"),
        [
            new Step(StepKind.Type, Spot.Css("#what")) { Value = "Acme" },
            new Step(StepKind.Click, Spot.Css("#go"))
            {
                Expected = new Effect(EffectKind.Request, "challenge.html")
            }
        ]));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Blocked, outcome.Failure.Kind);
        Assert.Contains("cf-browser-verification", outcome.Failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A step produces nothing because the site showed the door, and the outcome says so rather than calling
    /// the page empty.
    /// </summary>
    /// <remarks>
    /// The test above covers a walkthrough whose every step reacted; this covers the commoner shape, where the
    /// challenge is itself what stops a step from reacting. Measured against Google on 2026-09-13: the errand
    /// typed the query, the site accepted the submit and began loading the results, then redirected to a
    /// reCAPTCHA — and the outcome came back <see cref="FailureKind.Empty"/>, which is final and worth no
    /// second attempt, when the truth was <see cref="FailureKind.Blocked"/>, which is worth another identity.
    /// The refusal was named in the prose all along; the kind a caller branches on said the opposite.
    /// </remarks>
    [Fact]
    public async Task PerformAsync_StepThatProducedNothingBecauseOfAChallenge_ReportsBlockedRatherThanEmpty()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/turning.html"),
        [
            new Step(StepKind.Type, Spot.Css("#q")) { Value = "postgresql" },
            new Step(StepKind.Click, Spot.Css("#go"))
            {
                Attempts = 1,
                Expected = new Effect(EffectKind.Text, "results for postgresql")
                {
                    Within = TimeSpan.FromSeconds(3)
                }
            }
        ]));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Blocked, outcome.Failure.Kind);
        Assert.True(outcome.Failure.IsTransient);
        Assert.Contains("cf-browser-verification", outcome.Failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A form with no button is walked through by pressing a key, and a real list is chosen from.
    /// </summary>
    /// <remarks>
    /// Both moves exist because of a measurement. Asked on 2026-09-12 to drive five search engines by typing,
    /// the agent finished none of the three whose forms submit on the return key: on two the submitting
    /// element does not exist, and on the third a click on it sends nothing. The page here has no button at
    /// all, so an agent that cannot press a key cannot finish it; and its region is a native
    /// <c>select</c>, which the browser draws outside the page, so an option cannot simply be clicked.
    /// </remarks>
    [Fact]
    public async Task PerformAsync_AFormWithNoButtonAndARealList_IsStillWalkedThrough()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/keyed.html"),
        [
            new Step(StepKind.Type, Spot.Labelled("Ledger number")) { Value = "A-42" },
            new Step(StepKind.Choose, Spot.Css("#where")) { Value = "Northern" },
            new Step(StepKind.Press, Spot.Css("#what"))
            {
                Expected = new Effect(EffectKind.Text, "Ledger A-42 is held in Northern")
            }
        ]));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("Ledger A-42 is held in Northern", outcome.Value.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A native list hidden behind a widget of the site's own drawing is still chosen, because it is what
    /// the form submits.
    /// </summary>
    /// <remarks>
    /// Measured on the German register portal on 2026-09-12. Its lists are PrimeFaces widgets: the native
    /// <c>select</c> is clipped to a pixel behind a drawn label and panel, so the browser refused to choose on
    /// it as something nobody could see; and going through the panel instead — a hundred and fifty courts in
    /// a scrolling list — failed once in two runs when the panel did not open in time. The page here clips
    /// its list the same way, and the answer names the option's text, so a choice made on the wrong element
    /// is caught by the answer rather than passing silently.
    /// </remarks>
    [Fact]
    public async Task PerformAsync_AListHiddenBehindAWidget_IsStillChosenBecauseItIsWhatTheFormSubmits()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/widgeted.html"),
        [
            new Step(StepKind.Type, Spot.Css("#what")) { Value = "HRB 6684" },
            new Step(StepKind.Choose, Spot.Css("#where_input")) { Value = "Munich" },
            new Step(StepKind.Click, Spot.Css("#find"))
            {
                Expected = new Effect(EffectKind.Text, "Entry HRB 6684 at Munich")
            }
        ]));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("Entry HRB 6684 at Munich", outcome.Value.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A press with no key named is a press of the return key.
    /// </summary>
    /// <remarks>
    /// Written down as a test rather than left to the documentation because the default is the whole point of
    /// the move: the forms that need it are the ones with nothing to click.
    /// </remarks>
    [Fact]
    public async Task PerformAsync_APressWithoutANamedKey_SubmitsWithTheReturnKey()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/keyed.html"),
        [
            new Step(StepKind.Type, Spot.Css("#what")) { Value = "B-7" },
            new Step(StepKind.Choose, Spot.Css("#where")) { Value = "Southern" },
            new Step(StepKind.Press, Spot.Css("#what"))
            {
                Expected = new Effect(EffectKind.Text, "Ledger B-7 is held in Southern")
            }
        ]));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
    }

    /// <summary>An errand with no expectations still runs, for a form worth no ceremony.</summary>
    [Fact]
    public async Task PerformAsync_StepsTakenOnTrust_ReturnsWhateverThePageBecame()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/form.html"),
        [
            new Step(StepKind.Click, Spot.Css("#find"))
        ]));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("Nothing found", outcome.Value.Text, StringComparison.Ordinal);
    }

    private Harness Host()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddXUnit(output).SetMinimumLevel(LogLevel.Debug));
        services.AddPageAgent(options =>
        {
            options.BrowserServer = site.BrowserServer;
            options.MaxRetryAttempts = 0;
            options.NavigationTimeoutMs = 20_000;
        });

        services.Configure<HumanBehaviorOptions>(options => options.Enabled = false);
        services.Configure<PolitenessOptions>(options => options.MinDelayPerHost = TimeSpan.Zero);

        return new Harness(services.BuildServiceProvider());
    }

    private sealed class Harness(ServiceProvider provider) : IAsyncDisposable
    {
        public IPageAgent Agent { get; } = provider.GetRequiredService<IPageAgent>();

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
