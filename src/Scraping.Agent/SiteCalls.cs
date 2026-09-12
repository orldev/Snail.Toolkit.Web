using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Polly;
using Polly.Retry;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// Opens a page and lets it ask its own service on our behalf.
/// </summary>
/// <remarks>
/// The request is issued by <c>fetch</c> inside the page rather than by an HTTP client, because everything the
/// service checks — origin, cookies, the token the page minted — belongs to the page and cannot be reproduced
/// from outside. Measured on a registry on 2026-09-11: the same body sent over plain HTTP is refused outright,
/// while from inside the page it is answered in full.
/// </remarks>
public sealed class SiteCalls(
    IVisits visits,
    IIdentityRotation identities,
    IPoliteness politeness,
    ISiteRules rules,
    IBlockDetector blocks,
    IOptions<ScrapingOptions> options,
    ILogger<SiteCalls> logger) : ISiteCalls
{
    /// <summary>
    /// The retry a call gets, on the same terms the page reader has always had.
    /// </summary>
    /// <remarks>
    /// Measured on 2026-09-12: of five runs against a live registry through this class, four answered in
    /// eleven or twelve seconds and one died on a navigation timeout that never reproduced. The page reader
    /// survives exactly that, having carried a Polly pipeline from the start, while a call through the browser
    /// had none — so one hiccup lost the whole answer. The predicate is the same one, so the same rule
    /// decides: a refusal, a timeout, an unavailable browser or a proxy fault is worth another identity; a
    /// 404, an empty body or a robots ban is the site's final answer and is not asked twice.
    /// </remarks>
    private readonly ResiliencePipeline<Outcome<string, PageFailure>> _pipeline = BuildPipeline(options, logger);

    private const string Script =
        """
        async ([path, method, contentType, body, token, parameter]) => {
          const address = token ? path + (path.includes('?') ? '&' : '?') + parameter + '=' + encodeURIComponent(token) : path;
          const response = await fetch(address, {
            method,
            credentials: 'include',
            headers: { 'Content-Type': contentType, 'X-Requested-With': 'XMLHttpRequest' },
            body: method === 'GET' || method === 'HEAD' ? undefined : body
          });
          return JSON.stringify({ status: response.status, body: await response.text() });
        }
        """;

    private const string Minting =
        """
        ([key, action, within]) => new Promise((resolve, reject) => {
          if (!window.grecaptcha) { reject(new Error('the page carries no minting script')); return; }
          window.grecaptcha.ready(() => window.grecaptcha.execute(key, { action }).then(resolve).catch(reject));
          setTimeout(() => reject(new Error('minting timed out')), within);
        })
        """;

    /// <inheritdoc />
    public async Task<Outcome<string, PageFailure>> SendAsync(SiteCall call, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (call.Page.Address is not { } target)
            return Outcome<string, PageFailure>.Failed(new PageFailure(
                FailureKind.Navigation, "Not an absolute http or https address.") { Address = call.Page });

        return await _pipeline.ExecuteAsync(async token => await AttemptAsync(call, target, token), ct);
    }

    /// <summary>One whole attempt: a fresh identity, the site's own rules, its turn, and the call.</summary>
    /// <remarks>
    /// Everything a retry could change sits inside it. The identity is drawn per attempt, because that is what
    /// makes a second one worth making, and the robots ruling and the politeness gate follow that identity
    /// rather than one left over from an attempt that failed.
    /// </remarks>
    private async Task<Outcome<string, PageFailure>> AttemptAsync(SiteCall call, Uri target, CancellationToken ct)
    {
        var identity = identities.Next();

        if (!await rules.IsAllowedAsync(target, identity.Proxy, ct))
            return Outcome<string, PageFailure>.Failed(new PageFailure(
                FailureKind.RobotsDisallowed,
                "The site's robots.txt forbids this URL for the configured agent.") { Address = call.Page });

        await using var turn = await politeness.EnterAsync(
            target, await rules.DelayAsync(target, identity.Proxy, ct), ct);

        return await AskAsync(call, target, identity, ct);
    }

    /// <summary>
    /// The pipeline a call is made through, or none at all when retries are switched off.
    /// </summary>
    /// <remarks>
    /// Polly rejects a retry strategy configured with zero attempts, so «do not retry» has to be the absence
    /// of a strategy rather than a strategy of none — the same shape the page reader uses.
    /// </remarks>
    private static ResiliencePipeline<Outcome<string, PageFailure>> BuildPipeline(
        IOptions<ScrapingOptions> options,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = options.Value;

        if (settings.MaxRetryAttempts <= 0)
            return ResiliencePipeline<Outcome<string, PageFailure>>.Empty;

        return new ResiliencePipelineBuilder<Outcome<string, PageFailure>>()
            .AddRetry(new RetryStrategyOptions<Outcome<string, PageFailure>>
            {
                ShouldHandle = new PredicateBuilder<Outcome<string, PageFailure>>()
                    .HandleResult(outcome => !outcome.IsSuccess && outcome.Failure.IsTransient),
                MaxRetryAttempts = settings.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = settings.RetryDelay,
                OnRetry = arguments =>
                {
                    logger.LogWarning("Retry {Attempt} of a site call after {Failure}",
                        arguments.AttemptNumber + 1, arguments.Outcome.Result?.Failure);

                    return default;
                }
            })
            .Build();
    }

    private async Task<Outcome<string, PageFailure>> AskAsync(
        SiteCall call,
        Uri target,
        WebIdentity identity,
        CancellationToken ct)
    {
        try
        {
            await using var visit = await visits.OpenAsync(identity, ct);

            var page = visit.Page;
            var response = await page.GotoAsync(target.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            var verdict = blocks.Inspect(await page.ContentAsync(), page.Url);

            if (verdict.IsBlocked)
                return Refused(call, identity, FailureKind.Blocked,
                    $"The site answered with a challenge or refusal ({verdict.Signal}).", response?.Status);

            await Task.Delay(call.Settle, ct);

            var token = await TokenAsync(page, call, identity);

            if (token is { IsSuccess: false })
                return Outcome<string, PageFailure>.Failed(token.Failure);

            var answered = await page.EvaluateAsync<string>(Script,
                new object?[] { call.Path, call.Method, call.ContentType, call.Body, token?.Value, call.Mint?.Parameter });

            return Judge(call, identity, answered);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is PlaywrightException or System.TimeoutException)
        {
            return Refused(call, identity, FailureKind.Unexpected, exception.Message, null);
        }
    }

    private async Task<Outcome<string, PageFailure>?> TokenAsync(IPage page, SiteCall call, WebIdentity identity)
    {
        if (call.Mint is not { } mint)
            return null;

        try
        {
            var token = await page.EvaluateAsync<string>(Minting,
                new object[] { mint.Key, mint.Action, mint.Within.TotalMilliseconds });

            return Outcome<string, PageFailure>.Success(token);
        }
        catch (Exception exception) when (exception is PlaywrightException or System.TimeoutException)
        {
            logger.LogWarning(
                "The page would not mint a '{Action}' credential, so its service was never asked", mint.Action);

            return Refused(call, identity, FailureKind.Blocked,
                $"The page never issued a '{mint.Action}' credential: {exception.Message}", null);
        }
    }

    private Outcome<string, PageFailure> Judge(SiteCall call, WebIdentity identity, string answered)
    {
        using var document = JsonDocument.Parse(answered);

        var status = document.RootElement.GetProperty("status").GetInt32();
        var body = document.RootElement.GetProperty("body").GetString() ?? string.Empty;

        if (status >= 400)
            return Refused(call, identity, status is 401 or 403 ? FailureKind.Blocked : FailureKind.Navigation,
                $"The service answered {status}: {Trimmed(body)}", status);

        if (string.IsNullOrWhiteSpace(body))
            return Refused(call, identity, FailureKind.Empty,
                $"The service answered {status} with an empty body.", status);

        return Outcome<string, PageFailure>.Success(body);
    }

    private static Outcome<string, PageFailure> Refused(
        SiteCall call,
        WebIdentity identity,
        FailureKind kind,
        string message,
        int? status)
        => Outcome<string, PageFailure>.Failed(new PageFailure(kind, message)
        {
            Address = call.Page,
            StatusCode = status,
            UserAgent = identity.UserAgent
        });

    private static string Trimmed(string body)
        => body.Length <= 200 ? body : $"{body[..200]}…";
}
