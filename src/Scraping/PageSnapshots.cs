using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Pictures a page through the same visit the page reader makes.
/// </summary>
public sealed class PageSnapshots(
    IAttempts<PageSnapshot> attempts,
    IVisits visits,
    IBlockDetector blocks,
    IOptions<ScrapingOptions> options,
    TimeProvider clock,
    ILogger<PageSnapshots> logger) : IPageSnapshots
{
    /// <summary>The JPEG quality every picture is encoded at.</summary>
    public const int Quality = 80;

    /// <summary>How tall a picture of a whole page may be, in the page's own pixels.</summary>
    /// <remarks>
    /// A page is as long as its author likes, and an endless feed is longer than that. A picture of all of
    /// it would be tens of megabytes nobody can look at — a model scales a picture down to something near two
    /// thousand pixels a side — so the ceiling is a few screens, and <see cref="PageSnapshot.IsCut"/> says
    /// when it was reached.
    /// </remarks>
    public const int TallestPage = 8_000;

    private const string Measured =
        "() => [document.documentElement.scrollWidth, document.documentElement.scrollHeight, window.innerWidth, window.innerHeight]";

    private readonly ScrapingOptions _options = options.Value;

    /// <inheritdoc />
    public Task<Outcome<PageSnapshot, PageFailure>> TakeAsync(Link address, CancellationToken ct)
        => TakeAsync(address, SnapshotFrame.Screen, ct);

    /// <inheritdoc />
    public Task<Outcome<PageSnapshot, PageFailure>> TakeAsync(Link address, SnapshotFrame frame, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        return attempts.AtAsync(
            address,
            (identity, target, token) => PicturedAsync(address, target, identity, frame, token),
            ct);
    }

    private async Task<Outcome<PageSnapshot, PageFailure>> PicturedAsync(
        Link address,
        Uri target,
        WebIdentity identity,
        SnapshotFrame frame,
        CancellationToken ct)
    {
        try
        {
            await using var visit = await visits.OpenAsync(identity, Appetite.Everything, target, ct);
            var page = visit.Page;

            var response = await page.GotoAsync(target.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            await DrawnAsync(page, ct);
            ct.ThrowIfCancellationRequested();

            if (Refusal(address, identity, await page.ContentAsync(), page.Url, response?.Status) is { } refusal)
                return Outcome<PageSnapshot, PageFailure>.Failed(refusal);

            var size = await page.EvaluateAsync<int[]>(Measured);
            var taken = Taken(frame, size);
            var image = await page.ScreenshotAsync(taken.Options);

            return Outcome<PageSnapshot, PageFailure>.Success(new PageSnapshot(address, image, clock.GetUtcNow())
            {
                Final = Link.Of(page.Url),
                Title = Named(await page.TitleAsync()),
                StatusCode = response?.Status,
                Width = taken.Width,
                Height = taken.Height,
                IsCut = taken.Height < size[1]
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (BrowserFailure.IsBrowsers(exception))
        {
            return Outcome<PageSnapshot, PageFailure>.Failed(BrowserFailure.Of(exception, address, identity));
        }
        catch (Exception exception)
        {
            Log.PictureFailed(logger, exception, address);

            return Outcome<PageSnapshot, PageFailure>.Failed(new PageFailure(FailureKind.Unexpected, exception.Message)
            {
                Address = address,
                Cause = exception,
                UserAgent = identity.UserAgent
            });
        }
    }

    /// <summary>Waits for the page's own images, and takes what is drawn if they never all arrive.</summary>
    /// <remarks>
    /// The reader stops at the parsed document because text needs no more. A picture taken there is a page
    /// of empty frames, so this waits for the load event — but not for ever: a page holding one request open
    /// never fires it, and what is on the screen by then is still the picture that was asked for.
    /// </remarks>
    private async Task DrawnAsync(IPage page, CancellationToken ct)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions
            {
                Timeout = Math.Min(_options.NavigationTimeoutMs, 10_000)
            }).WaitAsync(ct);
        }
        catch (Exception exception) when (BrowserFailure.IsBrowsers(exception))
        {
            Log.NeverLoaded(logger, page.Url);
        }
    }

    private PageFailure? Refusal(Link address, WebIdentity identity, string html, string finalUrl, int? status)
    {
        var verdict = blocks.Inspect(html, finalUrl);

        if (verdict.IsBlocked)
            return new PageFailure(FailureKind.Blocked, $"The site answered with a challenge or refusal ({verdict.Signal}).")
            {
                Address = address,
                StatusCode = status,
                UserAgent = identity.UserAgent
            };

        return status >= 400
            ? new PageFailure(FailureKind.Navigation, $"The site answered with HTTP {status}.")
            {
                Address = address,
                StatusCode = status,
                UserAgent = identity.UserAgent
            }
            : null;
    }

    private static (PageScreenshotOptions Options, int Width, int Height) Taken(SnapshotFrame frame, int[] size)
    {
        var (pageWidth, pageHeight, screenWidth, screenHeight) = (size[0], size[1], size[2], size[3]);

        if (frame is SnapshotFrame.Screen)
            return (Encoded(), screenWidth, screenHeight);

        var height = Math.Min(pageHeight, TallestPage);
        var options = Encoded();

        options.FullPage = true;
        options.Clip = new Clip { X = 0, Y = 0, Width = pageWidth, Height = height };

        return (options, pageWidth, height);
    }

    private static PageScreenshotOptions Encoded() => new()
    {
        Type = ScreenshotType.Jpeg,
        Quality = Quality
    };

    private static string? Named(string title) => string.IsNullOrWhiteSpace(title) ? null : title.Trim();
}
