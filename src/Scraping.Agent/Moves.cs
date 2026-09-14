using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// Makes one move on a page and refuses to call it done until the site reacts.
/// </summary>
/// <remarks>
/// A move that goes through and changes nothing is the failure this exists to catch: a click on a button the
/// site ignores, a key the form does not listen for. Every step may therefore name what it expects to follow,
/// and until that shows up the move has not been made, however cleanly the browser performed it.
/// </remarks>
internal sealed class Moves(ILogger<Moves> logger)
{
    private const string Enter = "Enter";

    /// <summary>What the site did about a step: took it, ignored it, or never showed the element.</summary>
    public async Task<Reaction> TakenAsync(ILocator wanted, Step step, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(step);

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
}
