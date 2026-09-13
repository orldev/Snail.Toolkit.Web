using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Snail.Toolkit.Web.Diagnostics;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Scraping.Diagnostics;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Draws an identity, asks the site, waits for its turn, does the work, and repeats it where that is worth doing.
/// </summary>
/// <remarks>
/// The pipeline is built once, in the constructor, because this is a singleton per result type: an earlier
/// version of the same idiom was a field initializer on a scoped service, so a new Polly pipeline was assembled
/// for every scope. Retries are driven by <see cref="PageFailure.IsTransient"/> rather than by an empty answer,
/// so a consent wall is retried with a new identity while an empty document is accepted as the site's answer.
/// <para>
/// Measuring is here rather than in each caller because an attempt is what there is to measure, and because two
/// of the three callers had no instruments at all: an errand and a call to a site's own service each load a page
/// and neither was counted as one.
/// </para>
/// </remarks>
public sealed class Attempts<TValue> : IAttempts<TValue>
    where TValue : class
{
    private readonly IIdentityRotation _identities;
    private readonly IPoliteness _politeness;
    private readonly ISiteRules _rules;
    private readonly ILogger<Attempts<TValue>> _logger;
    private readonly ResiliencePipeline<Outcome<TValue, PageFailure>> _pipeline;

    /// <summary>Assembles the sequence and the retry pipeline it runs under.</summary>
    public Attempts(
        IIdentityRotation identities,
        IPoliteness politeness,
        ISiteRules rules,
        IOptions<ScrapingOptions> options,
        ILogger<Attempts<TValue>> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _identities = identities;
        _politeness = politeness;
        _rules = rules;
        _logger = logger;
        _pipeline = BuildPipeline(options.Value, logger);
    }

    /// <inheritdoc />
    public async Task<Outcome<TValue, PageFailure>> AtAsync(
        Link address,
        Func<WebIdentity, Uri, CancellationToken, Task<Outcome<TValue, PageFailure>>> work,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(work);

        if (address.Address is not { } target)
            return Outcome<TValue, PageFailure>.Failed(new PageFailure(
                FailureKind.Navigation, "Not an absolute http or https address.") { Address = address });

        return await _pipeline.ExecuteAsync(
            async token => await OnceAsync(address, target, work, token), ct);
    }

    private async ValueTask<Outcome<TValue, PageFailure>> OnceAsync(
        Link address,
        Uri target,
        Func<WebIdentity, Uri, CancellationToken, Task<Outcome<TValue, PageFailure>>> work,
        CancellationToken ct)
    {
        var identity = _identities.Next();
        var started = Stopwatch.GetTimestamp();

        using var activity = WebDiagnostics.Activity.StartActivity("web.read", ActivityKind.Client);
        activity?.SetTag("url.full", address.Canonical);
        activity?.SetTag("server.address", target.Host);
        activity?.SetTag("user_agent.original", identity.UserAgent);

        if (!await _rules.IsAllowedAsync(target, identity.Proxy, ct))
        {
            _logger.LogInformation("robots.txt forbids {Url}", address);

            return Recorded(Refused(address), target, started, activity);
        }

        await using var turn = await _politeness.EnterAsync(
            target,
            await _rules.DelayAsync(target, identity.Proxy, ct),
            ct);

        return Recorded(await work(identity, target, ct), target, started, activity);
    }

    private static Outcome<TValue, PageFailure> Refused(Link address)
        => Outcome<TValue, PageFailure>.Failed(new PageFailure(
            FailureKind.RobotsDisallowed,
            "The site's robots.txt forbids this URL for the configured agent.")
        {
            Address = address
        });

    private static Outcome<TValue, PageFailure> Recorded(
        Outcome<TValue, PageFailure> outcome,
        Uri target,
        long started,
        Activity? activity)
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

        return outcome;
    }

    /// <summary>Builds the retry pipeline, or none at all when retries are switched off.</summary>
    /// <remarks>
    /// Polly rejects a retry strategy configured with zero attempts, so "do not retry" has to be the absence of
    /// the strategy rather than a strategy that never fires.
    /// </remarks>
    private static ResiliencePipeline<Outcome<TValue, PageFailure>> BuildPipeline(
        ScrapingOptions options,
        ILogger logger)
    {
        if (options.MaxRetryAttempts <= 0)
            return ResiliencePipeline<Outcome<TValue, PageFailure>>.Empty;

        return new ResiliencePipelineBuilder<Outcome<TValue, PageFailure>>()
            .AddRetry(new RetryStrategyOptions<Outcome<TValue, PageFailure>>
            {
                ShouldHandle = new PredicateBuilder<Outcome<TValue, PageFailure>>()
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
