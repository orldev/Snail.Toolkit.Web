using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Probes;

/// <summary>
/// Measures whether a site counts requests rather than pages.
/// </summary>
/// <remarks>
/// A browser loading one results page makes seventy-odd requests; a command-line fetch makes one. Where a site
/// rates an address by requests, the budget is spent inside a single page load and the refusal looks like a
/// refusal of the client. Measured against Brave on 2026-09-14 this was exactly the case: the full page was
/// answered <c>429</c> three times out of three while the document alone was answered <c>200</c> three times
/// out of three, carrying all eighteen result blocks.
/// <para>
/// The arms alternate rather than running in two batches, so the order cannot be mistaken for the axis — that
/// mistake was made once here, when a lean arm placed first after a pause passed and looked like proof.
/// </para>
/// <para>
/// What is printed is the status, the request count and the number of result blocks, not "did it work": a
/// refusal and a page that parsed into nothing are different facts and a boolean would hide which happened.
/// </para>
/// </remarks>
internal static class Counting
{
    private const string Address = "https://search.brave.com/search?q=postgresql+documentation";
    private const string Blocks = "div[data-type='web']";

    public static async Task ReportAsync(string endpoint)
    {
        using var driver = await Playwright.CreateAsync();
        await using var browser = await driver.Firefox.ConnectAsync(endpoint, new BrowserTypeConnectOptions { Timeout = 30000 });

        Console.WriteLine($"{DateTime.Now:HH:mm} — считает ли движок запросы, а не страницы");

        var lean = 0;
        var full = 0;

        for (var round = 1; round <= 3; round++)
        {
            lean += await IsAnsweredAsync(browser, $"{round}. только документ  ", documentOnly: true) ? 1 : 0;

            await Task.Delay(TimeSpan.FromSeconds(45));

            full += await IsAnsweredAsync(browser, $"{round}. всё целиком      ", documentOnly: false) ? 1 : 0;

            if (round < 3)
                await Task.Delay(TimeSpan.FromSeconds(45));
        }

        Console.WriteLine($"  только документ          {lean} из 3");
        Console.WriteLine($"  всё целиком              {full} из 3");
    }

    private static async Task<bool> IsAnsweredAsync(IBrowser browser, string label, bool documentOnly)
    {
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
        });

        var asked = 0;

        if (documentOnly)
            await context.RouteAsync("**/*", async route =>
            {
                if (route.Request.ResourceType is "document")
                {
                    asked++;

                    await route.ContinueAsync();

                    return;
                }

                await route.AbortAsync();
            });
        else
            context.Request += (_, _) => asked++;

        var page = await context.NewPageAsync();

        try
        {
            var reply = await page.GotoAsync(Address, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });
            var status = reply?.Status ?? 0;
            var found = await page.Locator(Blocks).CountAsync();

            Console.WriteLine($"  {label} {status}, запросов {asked}, блоков выдачи {found}");

            return status == 200;
        }
        catch (PlaywrightException failure)
        {
            Console.WriteLine($"  {label} сорвалось: {failure.Message[..Math.Min(60, failure.Message.Length)]}");

            return false;
        }
    }
}
