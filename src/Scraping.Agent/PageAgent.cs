using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// Walks an errand through a page: opens it, takes each step in turn, and reads where it ended up.
/// </summary>
/// <remarks>
/// The ceremony before the first move — identity, robots, politeness, a prepared context — is not rebuilt here.
/// The context comes through <see cref="IVisits"/>, because the last defect in this area was a context that
/// announced one browser in its headers and another in script after the two were set in two places; everything
/// around the attempt comes through <see cref="IAttempts{TValue}"/>, which is also where the errand gained a
/// retry it never had — a browser that went away in the middle of a form used to lose the errand outright,
/// while the same failure in a plain page read had been retried since the beginning.
/// <para>
/// What is left here is the walk and nothing else. Finding the element a step is about lives in
/// <see cref="Spots"/>, making the move and waiting for the site to react in <see cref="Moves"/>, and saying
/// what a step that produced nothing produced instead in <see cref="Setbacks"/> — four hundred and ninety
/// lines that read as one class and behaved as four.
/// </para>
/// <para>
/// Internal, like the rest of that machinery and like the vocabulary it is written in
/// (<see cref="Reaction"/>, <see cref="Scene"/>, <see cref="Trail"/>). Consumers reach it through
/// <see cref="IPageAgent"/>, which is the only shape a caller has ever used it in, and the package's public
/// surface stays the errand a caller writes rather than the parts that carry it out.
/// </para>
/// <para>
/// A refusal is looked for three times, at three different doors: on arrival, at the step that stopped
/// producing anything, and on the page the walk ended on. Each of the three was added after a measurement in
/// which the other two were already there and the site still got through.
/// </para>
/// </remarks>
internal sealed class PageAgent(
    IAttempts<PageContent> attempts,
    IVisits visits,
    IBlockDetector blocks,
    PageContents contents,
    Moves moves,
    Setbacks setbacks,
    IOptions<AgentOptions> agent) : IPageAgent
{
    private readonly AgentOptions _agent = agent.Value;

    /// <inheritdoc />
    public Task<PageOutcome> PerformAsync(Errand errand, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(errand);

        return attempts.AtAsync(
            errand.Address,
            (identity, target, token) => WalkAsync(errand, target, identity, token),
            ct);
    }

    private async Task<PageOutcome> WalkAsync(Errand errand, Uri target, WebIdentity identity, CancellationToken ct)
    {
        try
        {
            await using var visit = await visits.OpenAsync(identity, errand.Appetite, ct);

            var page = visit.Page;
            var response = await page.GotoAsync(target.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            // The gap the landing page asked for, carried into whichever refusal is recorded below. NOT OBSERVED
            // BY ANY TEST, and named here rather than left to be discovered: the reading itself is held by
            // RestsTests and the service call's by SiteCallJudgementTests, but these two assignments need a page
            // answering 429 with the header, which means a browser and a server that sets headers — the fixture
            // serves files. What would catch a regression here is the consumer's report naming a gap.
            var asked = Rests.AskedFor(response?.Headers, DateTimeOffset.UtcNow);

            var landing = await RefusedAsync(page, errand, identity, response?.Status, asked);

            if (landing is not null)
                return landing;

            for (var position = 0; position < errand.Steps.Count; position++)
            {
                ct.ThrowIfCancellationRequested();

                var step = errand.Steps[position];
                var wanted = await Spots.LocatedAsync(page, step.Where);

                using var trail = Trail.Follows(page);

                var reaction = await moves.TakenAsync(wanted, step, ct);

                if (reaction is Reaction.Taken)
                    continue;

                return PageOutcome.Failed(await setbacks.ExplainedAsync(
                    page, errand, identity, position + 1, reaction, wanted, trail));
            }

            await Task.Delay(_agent.Settle, ct);

            return await ReadAsync(page, errand, identity, response?.Status, asked);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (BrowserFailure.IsBrowsers(exception))
        {
            return PageOutcome.Failed(BrowserFailure.Of(exception, errand.Address, identity));
        }
    }

    private async Task<PageOutcome?> RefusedAsync(
        IPage page,
        Errand errand,
        WebIdentity identity,
        int? status,
        TimeSpan? asked)
    {
        var verdict = blocks.Inspect(await page.ContentAsync(), page.Url);

        if (!verdict.IsBlocked)
            return null;

        return PageOutcome.Failed(new PageFailure(
            FailureKind.Blocked, $"The site answered with a challenge or refusal ({verdict.Signal}).")
        {
            Address = errand.Address,
            StatusCode = status,
            UserAgent = identity.UserAgent,
            RetryAfter = asked
        });
    }

    /// <summary>Reads the page the walkthrough ended on, once it is established that it is that page.</summary>
    /// <remarks>
    /// The document is inspected for a refusal here as well as on arrival, because a walkthrough can be shown
    /// the door by the very move it was written to make. Measured against a search engine on 2026-09-12: every
    /// step succeeded, the site accepted the submit, began loading the results and then redirected to a
    /// challenge — and the agent, having checked only the landing page, reported success and handed back two
    /// thousand characters of "prove you are not a robot" as the answer to the errand.
    /// </remarks>
    private async Task<PageOutcome> ReadAsync(
        IPage page,
        Errand errand,
        WebIdentity identity,
        int? status,
        TimeSpan? asked)
    {
        var html = await page.ContentAsync();
        var verdict = blocks.Inspect(html, page.Url);

        if (verdict.IsBlocked)
            return PageOutcome.Failed(new PageFailure(
                FailureKind.Blocked, $"The walkthrough ended on a challenge or refusal ({verdict.Signal}).")
            {
                Address = errand.Address,
                StatusCode = status,
                UserAgent = identity.UserAgent,
                RetryAfter = asked
            });

        return contents.Judged(new Document
        {
            Address = errand.Address,
            Identity = identity,
            Html = html,
            Rendered = await page.EvaluateAsync<string?>("() => document.body ? document.body.innerText : ''"),
            FinalUrl = page.Url,
            StatusCode = status
        });
    }
}
