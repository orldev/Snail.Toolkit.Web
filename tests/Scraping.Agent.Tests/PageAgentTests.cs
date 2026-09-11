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
            new Step(StepKind.Type, "#party")
            {
                Value = "Acme",
                Expected = new Effect(EffectKind.Request, "suggest.json")
            },
            new Step(StepKind.Pick, "#hints li")
            {
                Value = "Acme Holdings",
                Expected = new Effect(EffectKind.Vanishes, "#hints")
            },
            new Step(StepKind.Click, "#find")
            {
                Expected = new Effect(EffectKind.Text, "Case A40-1/2026")
            }
        ]));

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        Assert.Contains("Case A40-1/2026 for Acme Holdings", outcome.Value.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A step the site never reacted to is named, rather than reported as a page with nothing on it.
    /// </summary>
    /// <remarks>
    /// This is the failure the package exists for. The page loads, every locator resolves, the click lands, and
    /// the result is still worthless — and an agent that only reports whether it executed its moves cannot tell
    /// that apart from a search that genuinely found nothing.
    /// </remarks>
    [Fact]
    public async Task PerformAsync_StepTheSiteIgnores_NamesTheStepInsteadOfReturningThePage()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/form.html"),
        [
            new Step(StepKind.Click, "#find")
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
    }

    /// <summary>An errand with no expectations still runs, for a form worth no ceremony.</summary>
    [Fact]
    public async Task PerformAsync_StepsTakenOnTrust_ReturnsWhateverThePageBecame()
    {
        await using var host = Host();

        var outcome = await host.Agent.PerformAsync(new Errand(Link.Of($"{FormFixture.BaseUrl}/form.html"),
        [
            new Step(StepKind.Click, "#find")
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
