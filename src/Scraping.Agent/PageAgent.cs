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
/// The ceremony before the first move — identity, robots, politeness, a prepared context — is not rebuilt here.
/// The context comes through <see cref="IVisits"/>, because the last defect in this area was a context that
/// announced one browser in its headers and another in script after the two were set in two places; everything
/// around the attempt comes through <see cref="IAttempts{TValue}"/>, which is also where the errand gained a
/// retry it never had — a browser that went away in the middle of a form used to lose the errand outright,
/// while the same failure in a plain page read had been retried since the beginning.
/// </remarks>
public sealed class PageAgent(
    IAttempts<PageContent> attempts,
    IVisits visits,
    IBlockDetector blocks,
    ITextExtraction text,
    IOptions<ScrapingOptions> scraping,
    IOptions<AgentOptions> agent,
    ILogger<PageAgent> logger) : IPageAgent
{
    private const int Listed = 6;

    private const string Enter = "Enter";

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

    private readonly AgentOptions _agent = agent.Value;

    private readonly ContentBudget _budget = scraping.Value.MaxContentCharacters > 0
        ? ContentBudget.Of(scraping.Value.MaxContentCharacters)
        : ContentBudget.Unlimited;

    /// <inheritdoc />
    public Task<PageOutcome> PerformAsync(Errand errand, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(errand);

        return attempts.AtAsync(
            errand.Address,
            (identity, target, token) => WalkAsync(errand, target, identity, token),
            ct);
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
                var wanted = await LocatedAsync(page, step.Where);

                using var trail = Trail.Follows(page);

                var reaction = await TakenAsync(wanted, step, ct);

                if (reaction is Reaction.Taken)
                    continue;

                return PageOutcome.Failed(
                    await FruitlessAsync(page, errand, identity, step, position + 1, reaction, wanted, trail));
            }

            await Task.Delay(_agent.Settle, ct);

            return await ReadAsync(page, errand, identity, response?.Status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (BrowserFailure.IsBrowsers(exception))
        {
            return PageOutcome.Failed(BrowserFailure.Of(exception, errand.Address, identity));
        }
    }

    /// <summary>Resolves where a step lands, by selector or by the caption that names the field.</summary>
    /// <remarks>
    /// Playwright's own <see cref="IPage.GetByLabel(string, PageGetByLabelOptions)"/> is tried first and is
    /// right whenever the markup binds a label to its field. It is not enough on its own: measured on a live
    /// registry on 2026-09-12, the caption is a <c>label</c> element carrying no <c>for</c> attribute and no
    /// field inside it — the field sits in a sibling container — so that call matched nothing while the first
    /// field following the caption matched exactly one. The last candidate drops the requirement that the
    /// caption be a <c>label</c> at all, since plenty of forms caption a field with a plain <c>div</c>.
    /// </remarks>
    private static async Task<ILocator> LocatedAsync(IPage page, Spot spot)
    {
        if (spot.Kind is not SpotKind.Label)
            return page.Locator(spot.Value);

        foreach (var candidate in Candidates(page, spot.Value))
        {
            if (await candidate.CountAsync() > 0)
                return candidate;
        }

        return page.GetByLabel(spot.Value);
    }

    private static IEnumerable<ILocator> Candidates(IPage page, string caption)
    {
        yield return page.GetByLabel(caption);
        yield return page.Locator(Follows("label", caption));
        yield return page.Locator(Follows("*", caption));
    }

    /// <summary>The first field that follows a caption, taking the innermost element that carries its text.</summary>
    /// <remarks>
    /// <c>last()</c> is what makes the loose match safe: every ancestor of the caption contains its text too,
    /// and the innermost of them is the caption itself, which is the one whose following field is meant.
    /// </remarks>
    private static string Follows(string tag, string caption)
        => $"xpath=(//{tag}[contains(normalize-space(.), {Quoted(caption)})])[last()]"
           + "/following::*[self::input or self::select or self::textarea][1]";

    /// <summary>Writes a value as an XPath string literal.</summary>
    /// <remarks>
    /// XPath 1.0 has no escape character inside a literal, so a caption carrying both kinds of quote can only
    /// be expressed as a concatenation of the pieces between them.
    /// </remarks>
    private static string Quoted(string text)
    {
        if (!text.Contains('\'', StringComparison.Ordinal))
            return $"'{text}'";

        if (!text.Contains('"', StringComparison.Ordinal))
            return $"\"{text}\"";

        return $"concat({string.Join(", \"'\", ", text.Split('\'').Select(part => $"'{part}'"))})";
    }

    private async Task<Reaction> TakenAsync(ILocator wanted, Step step, CancellationToken ct)
    {
        if (!await AppearedAsync(wanted, step))
            return Reaction.Missing;

        for (var attempt = 1; attempt <= Math.Max(1, step.Attempts); attempt++)
        {
            if (await AttemptedAsync(wanted, step, ct))
                return Reaction.Taken;

            logger.LogDebug(
                "Attempt {Attempt} at {Kind} on {Where} left the site unchanged", attempt, step.Kind, step.Where);
        }

        return Reaction.Ignored;
    }

    /// <summary>Waits for the element to be on the page before anything is done to it.</summary>
    /// <remarks>
    /// Seen, for every move a person makes with the mouse or the keys. A native list is the exception: a
    /// site that draws its own dropdown hides the <c>select</c> behind it — off the screen, clipped to a
    /// pixel — and that hidden element is still the one the form submits, so for <see cref="StepKind.Choose"/>
    /// it only has to be there.
    /// </remarks>
    private static async Task<bool> AppearedAsync(ILocator wanted, Step step)
    {
        try
        {
            await wanted.First.WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = (float)step.Ready.TotalMilliseconds,
                State = step.Kind is StepKind.Choose ? WaitForSelectorState.Attached : WaitForSelectorState.Visible
            });

            return true;
        }
        catch (Exception exception) when (exception is PlaywrightException or System.TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Makes the move once and says whether the site answered for it.</summary>
    /// <remarks>
    /// Both the move and the wait are caught here, so an element that stopped matching is an attempt that
    /// produced nothing rather than an exception: the step is then retried and, if it stays fruitless, named.
    /// Playwright 1.62 raises <see cref="System.TimeoutException"/> from a locator wait — not the
    /// <see cref="PlaywrightException"/> its navigation failures use — and catching only the latter let a
    /// two-second wait escape as an unhandled exception.
    /// </remarks>
    private async Task<bool> AttemptedAsync(ILocator wanted, Step step, CancellationToken ct)
    {
        try
        {
            if (step.Expected is not { } expected)
            {
                await MoveAsync(wanted, step);

                return true;
            }

            if (expected.Kind is EffectKind.Request)
                return await RequestedAsync(wanted, step, expected, ct);

            await MoveAsync(wanted, step);

            return await ShowedAsync(wanted.Page, expected);
        }
        catch (Exception exception) when (exception is PlaywrightException or System.TimeoutException)
        {
            logger.LogDebug(exception, "{Kind} on {Where} did not go through", step.Kind, step.Where);

            return false;
        }
    }

    private static async Task<bool> RequestedAsync(ILocator wanted, Step step, Effect expected, CancellationToken ct)
    {
        var page = wanted.Page;
        var asked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Watch(object? sender, IRequest request)
        {
            if (request.Url.Contains(expected.Pattern, StringComparison.OrdinalIgnoreCase))
                asked.TrySetResult(true);
        }

        page.Request += Watch;

        try
        {
            await MoveAsync(wanted, step);

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

    /// <summary>Makes one move on the element the step names.</summary>
    /// <remarks>
    /// <see cref="StepKind.Press"/> defaults to Enter because that is the case it exists for: measured against
    /// five search engines on 2026-09-12, two of them offer no submitting element at all and a third ignores a
    /// click on the one it has. <see cref="StepKind.Choose"/> goes through the browser's own selection rather
    /// than through a click, since a native list is drawn outside the page and its options cannot be pressed;
    /// and it is made without the browser's check that the list can be seen, because the lists worth choosing
    /// from are mostly hidden — a site that draws its own dropdown (PrimeFaces, Select2 and their kind) keeps
    /// the native <c>select</c> clipped to a pixel behind it, and that is the element the form submits. The
    /// German register's form was the measurement, on 2026-09-12: its court list is a hundred and fifty items
    /// in a scrolling panel with a filter box, and going through the widget — a click to open the panel, a
    /// click on the item — failed once in two runs when the panel did not open in time; choosing on the
    /// hidden list is exact and needs no panel.
    /// </remarks>
    private static async Task MoveAsync(ILocator wanted, Step step)
    {
        if (step.Kind is StepKind.Click)
        {
            await wanted.First.ClickAsync();

            return;
        }

        if (step.Kind is StepKind.Press)
        {
            await wanted.First.PressAsync(step.Value is { Length: > 0 } key ? key : Enter);

            return;
        }

        if (step.Kind is StepKind.Choose)
        {
            await wanted.First.SelectOptionAsync(
                new SelectOptionValue { Label = step.Value ?? string.Empty },
                new LocatorSelectOptionOptions { Force = true });

            return;
        }

        if (step.Kind is StepKind.Pick)
        {
            await wanted
                .Filter(new LocatorFilterOptions { HasTextString = step.Value })
                .First
                .ClickAsync();

            return;
        }

        await wanted.First.ClickAsync();
        await wanted.First.PressSequentiallyAsync(step.Value ?? string.Empty, new LocatorPressSequentiallyOptions
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

    /// <summary>Reads the page the walkthrough ended on, once it is established that it is that page.</summary>
    /// <remarks>
    /// The document is inspected for a refusal here as well as on arrival, because a walkthrough can be shown
    /// the door by the very move it was written to make. Measured against a search engine on 2026-09-12: every
    /// step succeeded, the site accepted the submit, began loading the results and then redirected to a
    /// challenge — and the agent, having checked only the landing page, reported success and handed back two
    /// thousand characters of "prove you are not a robot" as the answer to the errand.
    /// </remarks>
    private async Task<PageOutcome> ReadAsync(IPage page, Errand errand, WebIdentity identity, int? status)
    {
        var html = await page.ContentAsync();
        var verdict = blocks.Inspect(html, page.Url);

        if (verdict.IsBlocked)
            return PageOutcome.Failed(new PageFailure(
                FailureKind.Blocked, $"The walkthrough ended on a challenge or refusal ({verdict.Signal}).")
            {
                Address = errand.Address,
                StatusCode = status,
                UserAgent = identity.UserAgent
            });

        var rendered = await page.EvaluateAsync<string?>("() => document.body ? document.body.innerText : ''");

        if (string.IsNullOrWhiteSpace(rendered))
            return PageOutcome.Failed(new PageFailure(FailureKind.Empty, "The page rendered no content.")
            {
                Address = errand.Address,
                StatusCode = status,
                UserAgent = identity.UserAgent
            });

        var budgeted = _budget.Apply(text.Clean(html));

        return PageOutcome.Success(new PageContent(errand.Address, html, budgeted.Text, DateTimeOffset.UtcNow)
        {
            Final = Link.Of(page.Url),
            StatusCode = status,
            UserAgent = identity.UserAgent,
            IsTruncated = budgeted.IsTruncated,
            OriginalLength = budgeted.OriginalLength
        });
    }

    /// <summary>Names the step that produced nothing, and shows the page it produced nothing on.</summary>
    /// <remarks>
    /// The state of the page is gathered here rather than left to the caller because by the time an outcome is
    /// read the browser is gone. Answering "what was on the page instead" by hand took five throwaway scripts
    /// on 2026-09-12, all of them asking what the agent already had in front of it.
    /// <para>
    /// The document is inspected for a refusal first, because a step stops producing anything the moment the
    /// site shows the door, and the two answers call for opposite reactions: a challenge is
    /// <see cref="FailureKind.Blocked"/> and worth another identity, while <see cref="FailureKind.Empty"/> is
    /// final and worth none. Measured against Google on 2026-09-13: the errand typed the query, the site
    /// accepted the submit and began loading the results, then redirected to a reCAPTCHA — and the outcome
    /// came back as a step that produced nothing, so the one fact worth acting on was left in the prose while
    /// the kind a caller branches on said the opposite. The arrival is checked in
    /// <see cref="RefusedAsync"/> and the finished page in <see cref="ReadAsync"/>; this is the third door,
    /// and it was the one standing open.
    /// </para>
    /// </remarks>
    private async Task<PageFailure> FruitlessAsync(
        IPage page,
        Errand errand,
        WebIdentity identity,
        Step step,
        int position,
        Reaction reaction,
        ILocator wanted,
        Trail trail)
    {
        var verdict = blocks.Inspect(await page.ContentAsync(), page.Url);
        var scene = await SceneAsync(page, wanted, trail);

        logger.LogWarning("Step {Position} of {Total} produced nothing. {Scene}", position, errand.Steps.Count, scene);

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

    private async Task<Scene> SceneAsync(IPage page, ILocator wanted, Trail trail)
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
