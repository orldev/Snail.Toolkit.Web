namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// A test that talks to a site on the public internet, skipped unless a browser server is offered.
/// </summary>
/// <remarks>
/// xunit 2.9 has no dynamic skip — <c>Assert.Skip</c> does not exist there — so the decision is made here, at
/// discovery. That matters more than the mechanism: a suite that reports the check as skipped is admitting it
/// did not run, while one that returns early and passes is claiming a check it never made.
/// <para>
/// One variable both enables the test and says which browser server to drive, so there is no way to switch it
/// on and leave it pointing nowhere. It doubles as the switch for live checks that need no browser at all —
/// the question it really answers is whether this suite may reach the public internet, and two switches would
/// only make it possible to be half enabled.
/// </para>
/// <para>
/// The browser-backed checks state their engine themselves and do not read <c>SNAIL_BROWSER_ENGINE</c>: that
/// variable belongs to the container fixtures and answers a different question. The image those start and the
/// server these drive legitimately differ — stock Chromium in a container beside the patched Gecko build on a
/// server — and reading one variable for both made that inexpressible.
/// </para>
/// <para>
/// WHAT A RUN SHOULD LOOK LIKE, measured on 2026-09-14. Twenty-seven checks carry this attribute: five each
/// for Bing, DuckDuckGo, Brave, Google and the browser server itself, and two for the pledge registry.
/// Switched off, all twenty-seven report SKIP and the whole suite is 482 passed, 27 skipped and none failed,
/// in about a minute and a half. Switched on, nothing is skipped and roughly a minute is added — six seconds
/// for the browser server, nine for the two HTTP engines, twenty-five for Google, about a minute for the
/// registry.
/// </para>
/// <para>
/// FIVE OF THOSE CHECKS WERE WRITTEN RED AND ARE NOW GREEN, and they are worth knowing about because they are
/// the only ones in the suite that execute the same state from more than one thread. They live in
/// <c>HostPolitenessLoadTests</c> and <c>BrowserPoolTests</c>, and what they name was invisible to the
/// compiler, to the analysers and to four hundred and eighty-seven green tests before them. Two of them fail
/// by probability rather than by rule — read their remarks before weakening either one.
/// </para>
/// <para>
/// When every one of them turns red at once, read
/// <c>Snail.Toolkit.Web.Scraping.Tests.LiveBrowserTests</c> first and the engines afterwards. It is the only
/// check here that asks what the server is rather than what a site answered, and a server that is not the
/// patched build fails every engine for a reason none of them can name.
/// </para>
/// <para>
/// Read those two numbers, not the failure count. A suite that finishes in ten seconds with "0 failed" and
/// twenty-seven skips has run none of the checks that talk to anyone, and says so only in the numbers nobody
/// looks at: that is exactly what happened on 2026-09-14, when the services these need had stopped between
/// one run and the next. A skip is honest about one test and silent about the run.
/// </para>
/// </remarks>
public sealed class LiveFactAttribute : FactAttribute
{
    /// <summary>The variable that enables a live test and names the browser server it drives.</summary>
    public const string Variable = "SNAIL_LIVE_BROWSER_SERVER";

    /// <summary>Skips the test unless a browser server is named in the environment.</summary>
    public LiveFactAttribute()
    {
        if (Server.Length == 0)
            Skip = $"Set {Variable} to a Playwright browser server (ws://…) to run the live checks.";
    }

    /// <summary>The browser server named in the environment, or an empty string when there is none.</summary>
    public static string Server => Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
}
