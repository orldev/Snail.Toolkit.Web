using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
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
    ILogger<SiteCalls> logger) : ISiteCalls
{
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

        var identity = identities.Next();

        if (!await rules.IsAllowedAsync(target, identity.Proxy, ct))
            return Outcome<string, PageFailure>.Failed(new PageFailure(
                FailureKind.RobotsDisallowed,
                "The site's robots.txt forbids this URL for the configured agent.") { Address = call.Page });

        await using var turn = await politeness.EnterAsync(
            target, await rules.DelayAsync(target, identity.Proxy, ct), ct);

        return await AskAsync(call, target, identity, ct);
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
