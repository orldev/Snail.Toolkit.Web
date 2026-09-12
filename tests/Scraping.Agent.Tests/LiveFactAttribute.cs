namespace Snail.Toolkit.Web.Scraping.Agent.Tests;

/// <summary>
/// A test that talks to a site on the public internet, skipped unless a browser server is offered.
/// </summary>
/// <remarks>
/// xunit 2.9 has no dynamic skip — <c>Assert.Skip</c> does not exist there — so the decision is made here, at
/// discovery. That matters more than the mechanism: a suite that reports the check as skipped is admitting it
/// did not run, while one that returns early and passes is claiming a check it never made.
/// <para>
/// One variable both enables the test and says which browser server to drive, so there is no way to switch it
/// on and leave it pointing nowhere.
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
