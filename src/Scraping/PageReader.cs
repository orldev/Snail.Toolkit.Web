using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
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
    public Task<PageOutcome> ReadAsync(Link address, CancellationToken ct)
        => ReadAsync(address, Appetite.Everything, ct);

    /// <inheritdoc />
    public Task<PageOutcome> ReadAsync(Link address, Appetite appetite, CancellationToken ct = default)
        => ReadAsync(address, appetite, contents.Reading, readyWhen: null, ct);

    /// <inheritdoc />
    Task<PageOutcome> IPageReader.ReadWhenReadyAsync(
        Link address,
        Appetite appetite,
        string readyWhen,
        CancellationToken ct)
        => ReadAsync(address, appetite, contents.Reading, readyWhen, ct);

    /// <summary>Reads <paramref name="address"/> whole, answering what <paramref name="reading"/> asks of the page.</summary>
    internal Task<PageOutcome> ReadAsync(Link address, Reading reading, CancellationToken ct)
        => ReadAsync(address, Appetite.Everything, reading, readyWhen: null, ct);

    private Task<PageOutcome> ReadAsync(
        Link address,
        Appetite appetite,
        Reading reading,
        string? readyWhen,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(address);

        return attempts.AtAsync(
            address,
            (identity, target, token) => FetchAsync(address, target, identity, appetite, reading, readyWhen, token),
            ct);
    }

    private async Task<PageOutcome> FetchAsync(
        Link address,
        Uri target,
        WebIdentity identity,
        Appetite appetite,
        Reading reading,
        string? readyWhen,
        CancellationToken ct)
    {
        try
        {
            await using var visit = await visits.OpenAsync(identity, appetite, target, ct);

            var page = visit.Page;

            Trace(page, address);

            var response = await page.GotoAsync(target.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            await SettleAsync(page, ct);
            await ReadyAsync(page, readyWhen, ct);
            await behavior.SimulateAsync(page, ct);

            ct.ThrowIfCancellationRequested();

            var html = await page.ContentAsync();
            var rendered = await page.EvaluateAsync<string?>("() => document.body ? document.body.innerText : ''");

            return Judge(address, identity, html, rendered, page.Url, response?.Status, Rests.AskedFor(response?.Headers, DateTimeOffset.UtcNow), reading);
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
            Log.ReadFailed(logger, exception, address);

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
            Log.NeverIdle(logger, page.Url);
        }
    }

    /// <summary>Waits for what the caller said the page is ready with, and reads the page as it stands if it never comes.</summary>
    /// <remarks>
    /// Bounded the way the picture's wait is, by the navigation budget and never past ten seconds: a page that
    /// never draws what was asked for is either a refusal or a redesign, and both are told apart afterwards by
    /// reading the page — waiting longer would only make either answer later.
    /// </remarks>
    private async Task ReadyAsync(IPage page, string? readyWhen, CancellationToken ct)
    {
        if (readyWhen is null)
            return;

        try
        {
            await page.Locator(readyWhen).First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = Math.Min(_options.NavigationTimeoutMs, 10_000)
            }).WaitAsync(ct);
        }
        catch (Exception exception) when (BrowserFailure.IsBrowsers(exception))
        {
            Log.NeverReady(logger, page.Url, readyWhen);
        }
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
        TimeSpan? restAskedFor,
        Reading reading)
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

        var document = new Document
        {
            Address = address,
            Identity = identity,
            Html = html,
            Rendered = rendered,
            FinalUrl = finalUrl,
            StatusCode = status
        };

        return contents.Judged(document, reading);
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

        page.PageError += (_, error) => Log.PageError(logger, address, error);
    }
}
