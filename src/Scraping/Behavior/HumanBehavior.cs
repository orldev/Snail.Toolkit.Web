using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Scraping.Behavior;

/// <summary>
/// Moves the mouse along a curve, scrolls in uneven steps and hovers something interesting.
/// </summary>
/// <remarks>
/// Bounded by <see cref="HumanBehaviorOptions.Budget"/>. Without it a page that never settles turned an
/// optional flourish into an unbounded wait, and the scroll script in particular was awaited with neither a
/// timeout nor a cancellation token.
/// </remarks>
public sealed class HumanBehavior(IOptions<HumanBehaviorOptions> options, ILogger<HumanBehavior> logger)
    : IHumanBehavior
{
    private readonly HumanBehaviorOptions _options = options.Value;

    /// <inheritdoc />
    public async Task SimulateAsync(IPage page, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!_options.Enabled)
            return;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_options.Budget);

        try
        {
            await MoveMouseAsync(page, budget.Token);
            await ScrollAsync(page, budget.Token);
            await PauseAsync(budget.Token);
            await HoverAsync(page, budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.OutOfBudget(logger, _options.Budget);
        }
        catch (PlaywrightException exception)
        {
            Log.StoppedEarly(logger, exception);
        }
    }

    private async Task MoveMouseAsync(IPage page, CancellationToken ct)
    {
        var viewport = page.ViewportSize;

        if (viewport is null)
            return;

        var moves = Random.Shared.Next(2, 5);

        for (var move = 0; move < moves && !page.IsClosed; move++)
        {
            var path = Curve(viewport.Width, viewport.Height, _options.MouseSteps);

            foreach (var (x, y) in path)
            {
                ct.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    return;

                await page.Mouse.MoveAsync(x, y);
                await Task.Delay(Random.Shared.Next(2, 8), ct);
            }

            await Task.Delay(Random.Shared.Next(80, 300), ct);
        }
    }

    private async Task ScrollAsync(IPage page, CancellationToken ct)
    {
        if (page.IsClosed)
            return;

        var scroll = page.EvaluateAsync(
            """
            (maxDistance) => new Promise((resolve) => {
                let travelled = 0;
                const step = () => {
                    const amount = Math.floor(Math.random() * 80) + 20;
                    window.scrollBy(0, amount);
                    travelled += amount;
                    if (travelled < maxDistance && travelled < document.body.scrollHeight) {
                        setTimeout(step, Math.random() * 150 + 50);
                    } else {
                        resolve();
                    }
                };
                step();
            })
            """,
            _options.MaxScrollDistance);

        await scroll.WaitAsync(ct);
    }

    private async Task HoverAsync(IPage page, CancellationToken ct)
    {
        if (page.IsClosed || Random.Shared.NextDouble() >= _options.HoverProbability)
            return;

        var targets = await page.QuerySelectorAllAsync("a, button, input, [role='button']").WaitAsync(ct);

        if (targets.Count == 0)
            return;

        var box = await targets[Random.Shared.Next(targets.Count)].BoundingBoxAsync().WaitAsync(ct);

        if (box is null || page.IsClosed)
            return;

        await page.Mouse.MoveAsync(
            box.X + (float)(box.Width * Random.Shared.NextDouble()),
            box.Y + (float)(box.Height * Random.Shared.NextDouble()));

        await Task.Delay(Random.Shared.Next(200, 600), ct);
    }

    private Task PauseAsync(CancellationToken ct)
        => Task.Delay(Random.Shared.Next(_options.MinPauseMs, _options.MaxPauseMs + 1), ct);

    /// <summary>Draws a cubic Bézier path that stays inside the viewport.</summary>
    /// <remarks>
    /// Control points are clamped because an earlier version offset them by up to 200 pixels without a bound,
    /// so a path routinely left the viewport and produced pointer coordinates no real device emits.
    /// </remarks>
    private static List<(float X, float Y)> Curve(int width, int height, int steps)
    {
        var start = (X: Random.Shared.Next(0, width), Y: Random.Shared.Next(0, height));
        var end = (X: Random.Shared.Next(0, width), Y: Random.Shared.Next(0, height));
        var first = Wander(start, width, height);
        var second = Wander(end, width, height);
        var path = new List<(float, float)>(steps + 1);

        for (var index = 0; index <= steps; index++)
        {
            var t = index / (double)steps;
            var u = 1 - t;

            var x = (u * u * u * start.X) + (3 * u * u * t * first.X) + (3 * u * t * t * second.X) + (t * t * t * end.X);
            var y = (u * u * u * start.Y) + (3 * u * u * t * first.Y) + (3 * u * t * t * second.Y) + (t * t * t * end.Y);

            path.Add(((float)x, (float)y));
        }

        return path;
    }

    private static (int X, int Y) Wander((int X, int Y) anchor, int width, int height)
        => (Math.Clamp(anchor.X + Random.Shared.Next(-150, 150), 0, width - 1),
            Math.Clamp(anchor.Y + Random.Shared.Next(-150, 150), 0, height - 1));
}
