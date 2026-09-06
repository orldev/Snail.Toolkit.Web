using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Polly;
using Polly.Retry;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Diagnostics;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Scraping.Behavior;
using Snail.Toolkit.Web.Scraping.Browsers;
using Snail.Toolkit.Web.Scraping.Diagnostics;
using Snail.Toolkit.Web.Scraping.Identity;
using Snail.Toolkit.Web.Scraping.Stealth;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Loads a page in a pooled browser under a freshly generated identity.
/// </summary>
/// <remarks>
/// The retry pipeline is built once, in the constructor, because this is a singleton: it was previously a field
/// initializer on a scoped service, so a new Polly pipeline was assembled for every scope. Retries are driven by
/// <see cref="PageFailure.IsTransient"/> rather than by an empty string, so a consent wall is retried with a new
/// identity while an empty document is accepted as the site's answer.
/// </remarks>
public sealed class PageReader : IPageReader
{
    private readonly IBrowserPool _pool;
    private readonly IIdentityRotation _identities;
    private readonly IStealthScript _stealth;
    private readonly IHumanBehavior _behavior;
    private readonly IBlockDetector _blocks;
    private readonly ITextExtraction _text;
    private readonly IPoliteness _politeness;
    private readonly ISiteRules _rules;
    private readonly ScrapingOptions _options;
    private readonly ContentBudget _budget;
    private readonly ILogger<PageReader> _logger;
    private readonly ResiliencePipeline<PageOutcome> _pipeline;

    /// <summary>Assembles the reader and its retry pipeline.</summary>
    public PageReader(
        IBrowserPool pool,
        IIdentityRotation identities,
        IStealthScript stealth,
        IHumanBehavior behavior,
        IBlockDetector blocks,
        ITextExtraction text,
        IPoliteness politeness,
        ISiteRules rules,
        IOptions<ScrapingOptions> options,
        ILogger<PageReader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _pool = pool;
        _identities = identities;
        _stealth = stealth;
        _behavior = behavior;
        _blocks = blocks;
        _text = text;
        _politeness = politeness;
        _rules = rules;
        _options = options.Value;
        _budget = _options.MaxContentCharacters > 0
            ? ContentBudget.Of(_options.MaxContentCharacters)
            : ContentBudget.Unlimited;
        _logger = logger;
        _pipeline = BuildPipeline(_options, logger);
    }

    /// <inheritdoc />
    public async Task<PageOutcome> ReadAsync(Link address, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.Address is not { } target)
            return PageOutcome.Failed(new PageFailure(
                FailureKind.Navigation, "Not an absolute http or https address.") { Address = address });

        return await _pipeline.ExecuteAsync(async token => await AttemptAsync(address, target, token), ct);
    }

    private async ValueTask<PageOutcome> AttemptAsync(Link address, Uri target, CancellationToken ct)
    {
        var identity = _identities.Next();
        var started = Stopwatch.GetTimestamp();

        using var activity = WebDiagnostics.Activity.StartActivity("web.read", ActivityKind.Client);
        activity?.SetTag("url.full", address.Canonical);
        activity?.SetTag("server.address", target.Host);
        activity?.SetTag("user_agent.original", identity.UserAgent);

        var refusal = await RefusalAsync(address, target, identity, ct);

        if (refusal is not null)
        {
            Record(target, refusal, started, activity);

            return refusal;
        }

        await using var turn = await _politeness.EnterAsync(
            target,
            await _rules.DelayAsync(target, identity.Proxy, ct),
            ct);

        var outcome = await FetchAsync(address, target, identity, ct);

        Record(target, outcome, started, activity);

        return outcome;
    }

    private async ValueTask<PageOutcome?> RefusalAsync(
        Link address,
        Uri target,
        WebIdentity identity,
        CancellationToken ct)
    {
        if (await _rules.IsAllowedAsync(target, identity.Proxy, ct))
            return null;

        _logger.LogInformation("robots.txt forbids {Url}", target);

        return PageOutcome.Failed(new PageFailure(
            FailureKind.RobotsDisallowed,
            "The site's robots.txt forbids this URL for the configured agent.")
        {
            Address = address
        });
    }

    private async ValueTask<PageOutcome> FetchAsync(
        Link address,
        Uri target,
        WebIdentity identity,
        CancellationToken ct)
    {
        var traits = BrowserTraits.For(identity);

        try
        {
            await using var lease = await _pool.RentAsync(ct);
            await using var context = await lease.Browser.NewContextAsync(
                BrowserContexts.For(identity, traits, _options.IgnoreCertificateErrors));

            context.SetDefaultNavigationTimeout(_options.NavigationTimeoutMs);
            context.SetDefaultTimeout(_options.NavigationTimeoutMs);

            await context.AddInitScriptAsync(_stealth.For(identity));

            var page = await context.NewPageAsync();
            Trace(page, target);

            var response = await page.GotoAsync(target.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            await SettleAsync(page, ct);
            await _behavior.SimulateAsync(page, ct);

            ct.ThrowIfCancellationRequested();

            var html = await page.ContentAsync();
            var rendered = await page.EvaluateAsync<string?>("() => document.body ? document.body.innerText : ''");

            return Judge(address, identity, html, rendered, page.Url, response?.Status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PlaywrightException exception)
        {
            return PageOutcome.Failed(Classify(exception, address, identity));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unclassified failure while reading {Url}", target);

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
            _logger.LogDebug("Network never went idle for {Url}; continuing with the loaded DOM", page.Url);
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
        var verdict = _blocks.Inspect(html, finalUrl);

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

        var budgeted = _budget.Apply(_text.Clean(html));

        return PageOutcome.Success(new PageContent(address, html, budgeted.Text, DateTimeOffset.UtcNow)
        {
            Final = Link.Of(finalUrl),
            StatusCode = status,
            UserAgent = identity.UserAgent,
            IsTruncated = budgeted.IsTruncated,
            OriginalLength = budgeted.OriginalLength
        });
    }

    /// <summary>Maps a Playwright failure onto a kind a caller can act on.</summary>
    /// <remarks>
    /// Matching on the message is not a shortcut: it is the only route Playwright leaves open. Version 1.62
    /// exposes exactly one public exception type, <see cref="PlaywrightException"/> — its
    /// <c>TargetClosedException</c> is declared <c>internal</c> and cannot be named by a consumer, and no
    /// timeout type exists at all. Earlier code caught <see cref="TimeoutException"/> from <c>System</c>, which
    /// Playwright never throws, so that branch was unreachable and every navigation timeout fell through to the
    /// catch-all and was logged as an unexpected error.
    /// </remarks>
    private static PageFailure Classify(PlaywrightException exception, Link address, WebIdentity identity)
    {
        var kind = exception.Message switch
        {
            var message when message.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
                => FailureKind.Timeout,
            var message when message.Contains("Target closed", StringComparison.OrdinalIgnoreCase)
                             || message.Contains("Browser closed", StringComparison.OrdinalIgnoreCase)
                             || message.Contains("has been closed", StringComparison.OrdinalIgnoreCase)
                => FailureKind.BrowserUnavailable,
            var message when message.Contains("ERR_PROXY", StringComparison.Ordinal)
                             || message.Contains("ERR_TUNNEL_CONNECTION_FAILED", StringComparison.Ordinal)
                => FailureKind.Proxy,
            _ => FailureKind.Navigation
        };

        return new PageFailure(kind, exception.Message)
        {
            Address = address,
            Cause = exception,
            UserAgent = identity.UserAgent
        };
    }

    private void Trace(IPage page, Uri target)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
            return;

        page.PageError += (_, error) => _logger.LogDebug("Page error on {Url}: {Error}", target, error);
    }

    private static void Record(Uri target, PageOutcome outcome, long started, Activity? activity)
    {
        var status = outcome.IsSuccess ? "ok" : outcome.Failure.Kind.ToString();
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        ScrapingDiagnostics.Pages.Add(1,
            new KeyValuePair<string, object?>("outcome", status),
            new KeyValuePair<string, object?>("server.address", target.Host));

        ScrapingDiagnostics.Duration.Record(elapsed, new KeyValuePair<string, object?>("outcome", status));

        activity?.SetTag("web.read.outcome", status);

        if (!outcome.IsSuccess)
            activity?.SetStatus(ActivityStatusCode.Error, outcome.Failure.Message);
    }

    /// <summary>Builds the retry pipeline, or none at all when retries are switched off.</summary>
    /// <remarks>
    /// Polly rejects a retry strategy configured with zero attempts, so "do not retry" has to be the absence of
    /// the strategy rather than a strategy that never fires.
    /// </remarks>
    private static ResiliencePipeline<PageOutcome> BuildPipeline(ScrapingOptions options, ILogger logger)
    {
        if (options.MaxRetryAttempts <= 0)
            return ResiliencePipeline<PageOutcome>.Empty;

        return new ResiliencePipelineBuilder<PageOutcome>()
            .AddRetry(new RetryStrategyOptions<PageOutcome>
            {
                ShouldHandle = new PredicateBuilder<PageOutcome>()
                    .HandleResult(outcome => !outcome.IsSuccess && outcome.Failure.IsTransient),
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryDelay,
                OnRetry = arguments =>
                {
                    logger.LogWarning("Retry {Attempt} after {Failure}",
                        arguments.AttemptNumber + 1, arguments.Outcome.Result?.Failure);

                    return default;
                }
            })
            .Build();
    }
}
