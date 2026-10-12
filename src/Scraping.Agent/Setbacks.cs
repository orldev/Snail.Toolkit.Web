using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// Names the step that produced nothing, and shows the page it produced nothing on.
/// </summary>
/// <remarks>
/// The state of the page is gathered here rather than left to the caller because by the time an outcome is read
/// the browser is gone. Answering "what was on the page instead" by hand took five throwaway scripts on
/// 2026-09-12, all of them asking what the agent already had in front of it.
/// </remarks>
internal sealed class Setbacks(IBlockDetector blocks, ILogger<Setbacks> logger)
{
    /// <summary>How many matches of a locator are worth looking at before the list says enough.</summary>
    private const int Listed = 6;

    private const string Editable =
        """
        () => [...document.querySelectorAll('input:not([type=hidden]),select,textarea')]
          .slice(0, 6)
          .map(field => {
            const named = field.id || field.name || field.getAttribute('aria-label')
              || field.placeholder || field.tagName.toLowerCase();
            return named + (field.offsetParent ? '' : ' (hidden)');
          })
        """;

    /// <summary>Why the step at this position produced nothing.</summary>
    /// <remarks>
    /// The document is inspected for a refusal first, because a step stops producing anything the moment the
    /// site shows the door, and the two answers call for opposite reactions: a challenge is
    /// <see cref="FailureKind.Blocked"/> and worth another identity, while <see cref="FailureKind.Empty"/> is
    /// final and worth none. Measured against Google on 2026-09-13: the errand typed the query, the site
    /// accepted the submit and began loading the results, then redirected to a reCAPTCHA — and the outcome came
    /// back as a step that produced nothing, so the one fact worth acting on was left in the prose while the
    /// kind a caller branches on said the opposite. The arrival and the finished page are each checked by the
    /// agent itself; this is the third door, and it was the one standing open.
    /// </remarks>
    public async Task<PageFailure> ExplainedAsync(
        IPage page,
        Errand errand,
        WebIdentity identity,
        int position,
        Reaction reaction,
        ILocator wanted,
        Trail trail)
    {
        var step = errand.Steps[position - 1];
        var verdict = blocks.Inspect(await page.ContentAsync(), page.Url);
        var scene = await SceneAsync(page, wanted, trail);

        Log.ProducedNothing(logger, position, errand.Steps.Count, scene);

        if (verdict.IsBlocked)
            return new PageFailure(
                FailureKind.Blocked,
                $"Step {position} of {errand.Steps.Count} produced nothing because the site had answered with a "
                + $"challenge or refusal ({verdict.Signal}). {scene}")
            {
                Address = errand.Address,
                UserAgent = identity.UserAgent
            };

        var said = reaction is Reaction.Missing
            ? $"never showed {step.Where} within {step.Ready.TotalSeconds:0.#}s"
            : $"did not react to {step.Kind} at {step.Where} after {Tried(step)}; expected {Wanted(step)}";

        return new PageFailure(
            FailureKind.Empty,
            $"Step {position} of {errand.Steps.Count}: the site {said}. {scene} The page may well have content, "
            + "but it is not the answer to this errand.")
        {
            Address = errand.Address,
            UserAgent = identity.UserAgent
        };
    }

    private static async Task<Scene> SceneAsync(IPage page, ILocator wanted, Trail trail)
    {
        var matched = await wanted.CountAsync();
        var visible = 0;

        for (var index = 0; index < Math.Min(matched, Listed); index++)
        {
            if (await wanted.Nth(index).IsVisibleAsync())
                visible++;
        }

        return new Scene(matched, visible, page.Url, await page.TitleAsync())
        {
            Fields = await page.EvaluateAsync<string[]>(Editable) ?? [],
            Asked = trail.Asked
        };
    }

    private static string Tried(Step step)
        => Math.Max(1, step.Attempts) == 1 ? "one attempt" : $"{Math.Max(1, step.Attempts)} attempts";

    private static string Wanted(Step step)
        => step.Expected is { } effect ? $"{effect.Kind} matching '{effect.Pattern}'" : "nothing";
}
