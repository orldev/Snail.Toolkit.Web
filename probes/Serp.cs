using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Probes;

/// <summary>
/// Opens a results page the way the engine will answer: through its own form.
/// </summary>
/// <remarks>
/// Shared by the probes that need a page of real results to work from. The recipe is not decoration: measured
/// on 2026-09-13 with a fresh profile per arm and twelve minutes between them, the results address answered
/// <c>/sorry/</c> where the same query typed into the form returned seven results, with a passing arm on
/// either side of the refusal. The delays are the ones that were measured to pass, not the smallest ones that
/// might.
/// </remarks>
internal static class Serp
{
    private const string Field = "textarea[name=\"q\"], input[name=\"q\"]";

    public static async Task<IPage> OpenAsync(IBrowser browser, string query, string home = "https://www.google.com/")
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
        });

        var page = await context.NewPageAsync();

        await page.GotoAsync(home, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });
        await page.WaitForTimeoutAsync(2500);

        var field = page.Locator(Field).First;

        await field.ClickAsync();
        await page.WaitForTimeoutAsync(500);
        await field.PressSequentiallyAsync(query, new LocatorPressSequentiallyOptions { Delay = 140 });
        await page.WaitForTimeoutAsync(800);
        await field.PressAsync("Enter");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 45000 });
        await page.WaitForTimeoutAsync(3500);

        return page;
    }

    /// <summary>Whether the engine answered with its refusal page rather than with results.</summary>
    public static bool IsRefusal(IPage page) => page.Url.Contains("/sorry/", StringComparison.Ordinal);

    /// <summary>The links that carry a heading, which is the one shape that survives this engine's layout changes.</summary>
    public static Task<string[]> AnchorsAsync(IPage page)
        => page.Locator("a:has(h3)").EvaluateAllAsync<string[]>("nodes => nodes.map(n => n.getAttribute('href'))");
}
