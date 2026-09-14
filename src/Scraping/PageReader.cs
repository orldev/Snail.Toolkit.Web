using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Behavior;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Loads a page in a pooled browser under a freshly generated identity.
/// </summary>
/// <remarks>
/// What surrounds the fetch — the identity, the site's ruling on it, its turn at the per-host gate, the retry
/// when the answer was worth asking again for — belongs to <see cref="IAttempts{TValue}"/> and is not restated
/// here. This class is what happens once all of that has been arranged: open a page, read it, and say what it
/// turned out to be.
/// </remarks>
public sealed class PageReader(
    IAttempts<PageContent> attempts,
    IVisits visits,
    IHumanBehavior behavior,
    IBlockDetector blocks,
    PageContents contents,
    IOptions<ScrapingOptions> options,
    ILogger<PageReader> logger) : IPageReader
{
    private readonly ScrapingOptions _options = options.Value;

    /// <inheritdoc />
    public Task<PageOutcome> ReadAsync(Link address, CancellationToken ct = default)
        => ReadAsync(address, Appetite.Everything, ct);

    /// <inheritdoc />
    public Task<PageOutcome> ReadAsync(Link address, Appetite appetite, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        return attempts.AtAsync(
            address,
            (identity, target, token) => FetchAsync(address, target, identity, appetite, token),
            ct);
    }

    private async Task<PageOutcome> FetchAsync(
        Link address,
        Uri target,
        WebIdentity identity,
        Appetite appetite,
        CancellationToken ct)
    {
        try
        {
            await using var visit = await visits.OpenAsync(identity, appetite, ct);

            var page = visit.Page;

            Trace(page, address);

            var response = await page.GotoAsync(target.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            await SettleAsync(page, ct);
            await behavior.SimulateAsync(page, ct);

            ct.ThrowIfCancellationRequested();

            var html = await page.ContentAsync();
            var rendered = await page.EvaluateAsync<string?>("() => document.body ? document.body.innerText : ''");

            return Judge(address, identity, html, rendered, page.Url, response?.Status, RestAskedFor(response));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (BrowserFailure.IsBrowsers(exception))
        {
            return PageOutcome.Failed(BrowserFailure.Of(exception, address, identity));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unclassified failure while reading {Url}", address);

            return PageOutcome.Failed(new PageFailure(FailureKind.Unexpected, exception.Message)
            {
                Address = address,
                Cause = exception,
                UserAgent = identity.UserAgent
            });
        }
    }

    private async Task SettleAsync(IPage page, CancellationToken ct)
    {
        if (!_options.WaitForNetworkIdle)
            return;

        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle).WaitAsync(ct);
        }
        catch (PlaywrightException)
        {
            logger.LogDebug("Network never went idle for {Url}; continuing with the loaded DOM", page.Url);
        }
    }

    /// <summary>Reads the gap the site asked for, in either of the two forms the header is written in.</summary>
    /// <remarks>
    /// <c>Retry-After</c> carries either a count of seconds or an HTTP date, and both are common. A date in the
    /// past is read as no wait rather than a negative one, which is what a clock a few seconds out produces.
    /// </remarks>
    private static TimeSpan? RestAskedFor(IResponse? response)
    {
        if (response is null || !response.Headers.TryGetValue("retry-after", out var asked))
            return null;

        if (int.TryParse(asked, out var seconds))
            return seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;

        if (!DateTimeOffset.TryParse(asked, CultureInfo.InvariantCulture, out var until))
            return null;

        var gap = until - DateTimeOffset.UtcNow;

        return gap > TimeSpan.Zero ? gap : null;
    }

    /// <summary>Decides what the fetched document actually is.</summary>
    /// <remarks>
    /// The order matters. A challenge is recognised before the status, because a wall is often served with 403
    /// and calling it a navigation failure would hide the one fact worth acting on. Everything past those two
    /// questions is the same for a page read by address and a page walked to through a form, and lives in
    /// <see cref="PageContents"/> so that it can only have one answer.
    /// </remarks>
    private PageOutcome Judge(
        Link address,
        WebIdentity identity,
        string html,
        string? rendered,
        string finalUrl,
        int? status,
        TimeSpan? restAskedFor)
    {
        var verdict = blocks.Inspect(html, finalUrl);

        if (verdict.IsBlocked)
            return PageOutcome.Failed(new PageFailure(FailureKind.Blocked,
                $"The site answered with a challenge or refusal ({verdict.Signal}).")
            {
                Address = address,
                StatusCode = status,
                UserAgent = identity.UserAgent,
                RetryAfter = restAskedFor
            });

        if (status >= 400)
            return PageOutcome.Failed(new PageFailure(FailureKind.Navigation, $"The site answered with HTTP {status}.")
            {
                Address = address,
                StatusCode = status,
                UserAgent = identity.UserAgent,
                RetryAfter = restAskedFor
            });

        return contents.Judged(new Document
        {
            Address = address,
            Identity = identity,
            Html = html,
            Rendered = rendered,
            FinalUrl = finalUrl,
            StatusCode = status
        });
    }

    /// <summary>Follows the page's own script errors while the read lasts.</summary>
    /// <remarks>
    /// The link is named rather than the <see cref="Uri"/>, because a <see cref="Uri"/> prints the credentials
    /// it was written with and a <see cref="Link"/> does not.
    /// </remarks>
    private void Trace(IPage page, Link address)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
            return;

        page.PageError += (_, error) => logger.LogDebug("Page error on {Url}: {Error}", address, error);
    }
}
