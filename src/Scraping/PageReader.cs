using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Behavior;
using Snail.Toolkit.Web.Scraping.Browsers;
using Snail.Toolkit.Web.Text;

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
    ITextExtraction text,
    IOptions<ScrapingOptions> options,
    ILogger<PageReader> logger) : IPageReader
{
    private readonly ScrapingOptions _options = options.Value;

    private readonly ContentBudget _budget = options.Value.MaxContentCharacters > 0
        ? ContentBudget.Of(options.Value.MaxContentCharacters)
        : ContentBudget.Unlimited;

    /// <inheritdoc />
    public Task<PageOutcome> ReadAsync(Link address, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        return attempts.AtAsync(
            address,
            (identity, target, token) => FetchAsync(address, target, identity, token),
            ct);
    }

    private async Task<PageOutcome> FetchAsync(
        Link address,
        Uri target,
        WebIdentity identity,
        CancellationToken ct)
    {
        try
        {
            await using var visit = await visits.OpenAsync(identity, ct);

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

            return Judge(address, identity, html, rendered, page.Url, response?.Status);
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

    /// <summary>Decides what the fetched document actually is.</summary>
    /// <remarks>
    /// The order matters. A challenge is recognised before the status, because a wall is often served with 403
    /// and calling it a navigation failure would hide the one fact worth acting on. Emptiness is judged on the
    /// rendered text rather than the markup: a browser returns
    /// <c>&lt;html&gt;&lt;head&gt;&lt;/head&gt;&lt;body&gt;&lt;/body&gt;&lt;/html&gt;</c> for a blank page, so
    /// testing the markup for whitespace never finds one.
    /// <para>
    /// The budget lands on the extracted text and the markup goes back whole. Cutting markup by character ends
    /// the document mid-tag, and the callers that read markup — every browser-backed search engine — then lose
    /// every link past the cut while the page still looks like it parsed.
    /// </para>
    /// </remarks>
    private PageOutcome Judge(
        Link address,
        WebIdentity identity,
        string html,
        string? rendered,
        string finalUrl,
        int? status)
    {
        var verdict = blocks.Inspect(html, finalUrl);

        if (verdict.IsBlocked)
            return PageOutcome.Failed(new PageFailure(FailureKind.Blocked,
                $"The site answered with a challenge or refusal ({verdict.Signal}).")
            {
                Address = address,
                StatusCode = status,
                UserAgent = identity.UserAgent
            });

        if (status >= 400)
            return PageOutcome.Failed(new PageFailure(FailureKind.Navigation, $"The site answered with HTTP {status}.")
            {
                Address = address,
                StatusCode = status,
                UserAgent = identity.UserAgent
            });

        if (string.IsNullOrWhiteSpace(rendered))
            return PageOutcome.Failed(new PageFailure(FailureKind.Empty, "The page rendered no content.")
            {
                Address = address,
                StatusCode = status,
                UserAgent = identity.UserAgent
            });

        var budgeted = _budget.Apply(text.Clean(html));

        return PageOutcome.Success(new PageContent(address, html, budgeted.Text, DateTimeOffset.UtcNow)
        {
            Final = Link.Of(finalUrl),
            Title = text.Title(html),
            StatusCode = status,
            UserAgent = identity.UserAgent,
            IsTruncated = budgeted.IsTruncated,
            OriginalLength = budgeted.OriginalLength
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
