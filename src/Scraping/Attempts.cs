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
        IOptions<PolitenessOptions> pacing,
        ILogger<Attempts<TValue>> logger)
        : this(identities, politeness, rules, options, pacing, logger, Repeats(options))
    {
    }

    /// <summary>Assembles the sequence for a caller that decides for itself what a refusal costs.</summary>
    /// <param name="identities">Where each attempt's identity comes from.</param>
    /// <param name="politeness">The per-host gate.</param>
    /// <param name="rules">The site's own rules.</param>
    /// <param name="options">How often to repeat, and how long to wait between.</param>
    /// <param name="pacing">The longest gap a site may ask for and still be waited out.</param>
    /// <param name="logger">Where retries are reported.</param>
    /// <param name="repeatsRefusals">
    /// Whether a refusal that names no gap is repeated. A crawl repeats it with a new identity, which is what
    /// a wall asks of a reader, and a deployment that knows better says so in
    /// <see cref="ScrapingOptions.RepeatsRefusals"/>. A search engine never does: the engines measured on
    /// 2026-09-12 closed over frequency, not fingerprint, and the search rests an engine the moment it hears
    /// the refusal — which a retry here would delay by half a minute and three more requests.
    /// </param>
    internal Attempts(
        IIdentityRotation identities,
        IPoliteness politeness,
        ISiteRules rules,
        IOptions<ScrapingOptions> options,
        IOptions<PolitenessOptions> pacing,
        ILogger<Attempts<TValue>> logger,
        bool repeatsRefusals)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pacing);

        _identities = identities;
        _politeness = politeness;
        _rules = rules;
        _logger = logger;
        _pipeline = BuildPipeline(options.Value, pacing.Value.LongestRest, repeatsRefusals, logger);
    }

    /// <summary>What the settings say about repeating a refusal, read before the fields are set.</summary>
    /// <remarks>
    /// A method rather than <c>options.Value.RepeatsRefusals</c> in the initializer, so that a caller handing
    /// over nothing meets <see cref="ArgumentNullException"/> naming the parameter instead of a dereference
    /// inside a constructor chain.
    /// </remarks>
    private static bool Repeats(IOptions<ScrapingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Value.RepeatsRefusals;
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

        var outcome = await work(identity, target, ct);

        Rest(target, outcome);

        return Recorded(outcome, target, started, activity);
    }

    /// <summary>Answers whether repeating the work could do better than repeating the refusal.</summary>
    /// <remarks>
    /// A site that names a gap has answered for the next several seconds, and the only question left is
    /// whether this caller waits it out. Past <paramref name="longestRest"/> it does not: measured on
    /// 2026-09-14, a thirty-second gap inside a twenty-second budget made one request, spent the whole budget
    /// waiting in the gate and ended in a cancellation carrying neither the status nor the number — while the
    /// first answer had already said both. The gate still holds the host for the full gap; what changes is
    /// that the refusal reaches the caller at once instead of being sat on.
    /// </remarks>
    private static bool IsWorthRepeating(
        Outcome<TValue, PageFailure> outcome,
        TimeSpan longestRest,
        bool repeatsRefusals)
    {
        if (outcome.IsSuccess || !outcome.Failure.IsTransient)
            return false;

        if (outcome.Failure.RetryAfter is { } gap)
            return gap <= longestRest;

        return repeatsRefusals || !outcome.Failure.IsRefusal;
    }

    /// <summary>Passes on a gap the site asked for, so the next caller waits rather than this one alone.</summary>
    /// <remarks>
    /// Written into the gate rather than into the retry delay, because the two are not the same promise. A
    /// retry delay binds this operation; the gate binds the host, and a <c>429</c> answered to one worker is
    /// not a fact about that worker. The retry then needs nothing of its own — every attempt enters the gate,
    /// and finds it closed for as long as the site asked.
    /// </remarks>
    private void Rest(Uri target, Outcome<TValue, PageFailure> outcome)
    {
        if (outcome.IsSuccess || outcome.Failure.RetryAfter is not { } gap)
            return;

        _logger.LogInformation(
            "{Host} asked to be left alone for {Gap}; holding it for every caller, not just this one.",
            target.Host, gap);

        _politeness.Rest(target, gap);
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
        TimeSpan longestRest,
        bool repeatsRefusals,
        ILogger logger)
    {
        if (options.MaxRetryAttempts <= 0)
            return ResiliencePipeline<Outcome<TValue, PageFailure>>.Empty;

        return new ResiliencePipelineBuilder<Outcome<TValue, PageFailure>>()
            .AddRetry(new RetryStrategyOptions<Outcome<TValue, PageFailure>>
            {
                ShouldHandle = new PredicateBuilder<Outcome<TValue, PageFailure>>()
                    .HandleResult(outcome => IsWorthRepeating(outcome, longestRest, repeatsRefusals)),
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
