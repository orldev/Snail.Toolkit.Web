using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Scraping.Browsers;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// Opens a page, makes each move on it and refuses to call a move done until the site reacts.
/// </summary>
/// <remarks>
/// The ceremony before the first move — identity, robots, politeness, a prepared context — is the reader's, taken
/// through <see cref="IVisits"/> rather than rebuilt here, because the last defect in this area was a context that
/// announced one browser in its headers and another in script after the two were set in two places.
/// </remarks>
public sealed class PageAgent(
    IVisits visits,
    IIdentityRotation identities,
    IPoliteness politeness,
    ISiteRules rules,
    IBlockDetector blocks,
    ITextExtraction text,
    IOptions<ScrapingOptions> scraping,
    IOptions<AgentOptions> agent,
    ILogger<PageAgent> logger) : IPageAgent
{
    private readonly ScrapingOptions _scraping = scraping.Value;
    private readonly AgentOptions _agent = agent.Value;

    /// <inheritdoc />
    public async Task<PageOutcome> PerformAsync(Errand errand, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(errand);

        if (errand.Address.Address is not { } target)
            return PageOutcome.Failed(new PageFailure(
                FailureKind.Navigation, "Not an absolute http or https address.") { Address = errand.Address });

        var identity = identities.Next();

        if (!await rules.IsAllowedAsync(target, identity.Proxy, ct))
            return PageOutcome.Failed(new PageFailure(
                FailureKind.RobotsDisallowed,
                "The site's robots.txt forbids this URL for the configured agent.") { Address = errand.Address });

        await using var turn = await politeness.EnterAsync(
            target, await rules.DelayAsync(target, identity.Proxy, ct), ct);

        return await WalkAsync(errand, target, identity, ct);
    }

    private async Task<PageOutcome> WalkAsync(Errand errand, Uri target, WebIdentity identity, CancellationToken ct)
    {
        try
        {
            await using var visit = await visits.OpenAsync(identity, ct);

            var page = visit.Page;
            var response = await page.GotoAsync(target.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            var landing = await RefusedAsync(page, errand, identity, response?.Status);

            if (landing is not null)
                return landing;

            for (var position = 0; position < errand.Steps.Count; position++)
            {
                ct.ThrowIfCancellationRequested();

                var step = errand.Steps[position];

                if (await TakenAsync(page, step, ct))
                    continue;

                return PageOutcome.Failed(Fruitless(errand, identity, step, position + 1));
            }

            await Task.Delay(_agent.Settle, ct);

            return await ReadAsync(page, errand, identity, response?.Status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (System.TimeoutException exception)
        {
            return PageOutcome.Failed(new PageFailure(FailureKind.Timeout, exception.Message)
            {
                Address = errand.Address,
                Cause = exception,
                UserAgent = identity.UserAgent
            });
        }
        catch (PlaywrightException exception)
        {
            return PageOutcome.Failed(new PageFailure(FailureKind.Unexpected, exception.Message)
            {
                Address = errand.Address,
                Cause = exception,
                UserAgent = identity.UserAgent
            });
        }
    }

    private async Task<bool> TakenAsync(IPage page, Step step, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= Math.Max(1, step.Attempts); attempt++)
        {
            if (await AttemptedAsync(page, step, ct))
                return true;

            logger.LogDebug(
                "Attempt {Attempt} at {Kind} on '{Selector}' left the site unchanged", attempt, step.Kind, step.Selector);
        }

        return false;
    }

    /// <summary>Makes the move once and says whether the site answered for it.</summary>
    /// <remarks>
    /// Both the move and the wait are caught here, so an element that never appeared is an attempt that produced
    /// nothing rather than an exception: the step is then retried and, if it stays fruitless, named. Playwright
    /// 1.62 raises <see cref="System.TimeoutException"/> from a locator wait — not the
    /// <see cref="PlaywrightException"/> its navigation failures use — and catching only the latter let a
    /// two-second wait escape as an unhandled exception.
    /// </remarks>
    private async Task<bool> AttemptedAsync(IPage page, Step step, CancellationToken ct)
    {
        try
        {
            if (step.Expected is not { } expected)
            {
                await MoveAsync(page, step);

                return true;
            }

            if (expected.Kind is EffectKind.Request)
                return await RequestedAsync(page, step, expected, ct);

            await MoveAsync(page, step);

            return await ShowedAsync(page, expected);
        }
        catch (Exception exception) when (exception is PlaywrightException or System.TimeoutException)
        {
            logger.LogDebug(exception, "{Kind} on '{Selector}' did not go through", step.Kind, step.Selector);

            return false;
        }
    }

    private async Task<bool> RequestedAsync(IPage page, Step step, Effect expected, CancellationToken ct)
    {
        var asked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Watch(object? sender, IRequest request)
        {
            if (request.Url.Contains(expected.Pattern, StringComparison.OrdinalIgnoreCase))
                asked.TrySetResult(true);
        }

        page.Request += Watch;

        try
        {
            await MoveAsync(page, step);

            using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);

            window.CancelAfter(expected.Within);

            return await asked.Task.WaitAsync(window.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            page.Request -= Watch;
        }
    }

    private static async Task<bool> ShowedAsync(IPage page, Effect expected)
    {
        var waiting = new LocatorWaitForOptions
        {
            Timeout = (float)expected.Within.TotalMilliseconds,
            State = expected.Kind is EffectKind.Vanishes ? WaitForSelectorState.Hidden : WaitForSelectorState.Visible
        };

        var wanted = expected.Kind is EffectKind.Text
            ? page.GetByText(expected.Pattern).First
            : page.Locator(expected.Pattern).First;

        await wanted.WaitForAsync(waiting);

        return true;
    }

    private static async Task MoveAsync(IPage page, Step step)
    {
        var field = page.Locator(step.Selector).First;

        if (step.Kind is StepKind.Click)
        {
            await field.ClickAsync();

            return;
        }

        if (step.Kind is StepKind.Pick)
        {
            await page.Locator(step.Selector)
                .Filter(new LocatorFilterOptions { HasTextString = step.Value })
                .First
                .ClickAsync();

            return;
        }

        await field.ClickAsync();
        await field.PressSequentiallyAsync(step.Value ?? string.Empty, new LocatorPressSequentiallyOptions
        {
            Delay = (float)step.Rhythm.TotalMilliseconds
        });
    }

    private async Task<PageOutcome?> RefusedAsync(IPage page, Errand errand, WebIdentity identity, int? status)
    {
        var verdict = blocks.Inspect(await page.ContentAsync(), page.Url);

        if (!verdict.IsBlocked)
            return null;

        return PageOutcome.Failed(new PageFailure(
            FailureKind.Blocked, $"The site answered with a challenge or refusal ({verdict.Signal}).")
        {
            Address = errand.Address,
            StatusCode = status,
            UserAgent = identity.UserAgent
        });
    }

    private async Task<PageOutcome> ReadAsync(IPage page, Errand errand, WebIdentity identity, int? status)
    {
        var html = await page.ContentAsync();
        var rendered = await page.EvaluateAsync<string?>("() => document.body ? document.body.innerText : ''");

        if (string.IsNullOrWhiteSpace(rendered))
            return PageOutcome.Failed(new PageFailure(FailureKind.Empty, "The page rendered no content.")
            {
                Address = errand.Address,
                StatusCode = status,
                UserAgent = identity.UserAgent
            });

        var budget = _scraping.MaxContentCharacters > 0
            ? ContentBudget.Of(_scraping.MaxContentCharacters)
            : ContentBudget.Unlimited;

        var budgeted = budget.Apply(text.Clean(html));

        return PageOutcome.Success(new PageContent(errand.Address, html, budgeted.Text, DateTimeOffset.UtcNow)
        {
            Final = Link.Of(page.Url),
            StatusCode = status,
            UserAgent = identity.UserAgent,
            IsTruncated = budgeted.IsTruncated,
            OriginalLength = budgeted.OriginalLength
        });
    }

    private static PageFailure Fruitless(Errand errand, WebIdentity identity, Step step, int position)
    {
        var expected = step.Expected is { } effect
            ? $"{effect.Kind} matching '{effect.Pattern}'"
            : "nothing";

        return new PageFailure(
            FailureKind.Empty,
            $"Step {position} of {errand.Steps.Count} ({step.Kind} on '{step.Selector}') left the site unchanged "
            + $"after {Math.Max(1, step.Attempts)} attempts; expected {expected}. The page may well have content, "
            + "but it is not the answer to this errand.")
        {
            Address = errand.Address,
            UserAgent = identity.UserAgent
        };
    }
}
