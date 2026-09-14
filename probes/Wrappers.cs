using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Probes;

/// <summary>
/// Asks whether a wrapped result link can be opened without decrypting it, and whether a simpler layout helps.
/// </summary>
/// <remarks>
/// Google hands Gecko a relative <c>/goto?url=&lt;blob&gt;</c> for every result, and the blob is a protobuf
/// with an encrypted body that no client can read. The way out is not to read it: the server does, and says so
/// in a redirect. Measured on 2026-09-14, the wrapper answers <c>302</c> with the real address in
/// <c>Location</c>, and the body is never fetched.
/// <para>
/// The second arm is kept as a record of what does not work. <c>gbv=1</c> once asked for a basic layout; the
/// engine now accepts the parameter and rewrites it to <c>gbv=2</c> in its own answer, so the parameter is
/// alive and the value is dead. <c>udm=14</c> is worse than the ordinary page rather than better — it wraps
/// every link too and drops the data islands the ordinary page carries.
/// </para>
/// </remarks>
internal static class Wrappers
{
    private const string Query = "postgresql documentation";

    public static async Task ReportAsync(string endpoint)
    {
        using var driver = await Playwright.CreateAsync();
        await using var browser = await driver.Firefox.ConnectAsync(endpoint, new BrowserTypeConnectOptions { Timeout = 30000 });

        Console.WriteLine($"{DateTime.Now:HH:mm} — раскрывается ли обёртка ссылки");

        var page = await Serp.OpenAsync(browser, Query);

        if (Serp.IsRefusal(page))
        {
            Console.WriteLine("  выдача                   ОТКАЗ /sorry/ — замер не состоялся");

            return;
        }

        var serp = page.Url;
        var anchors = await Serp.AnchorsAsync(page);
        var wrapped = anchors.Where(href => href?.StartsWith('/') == true).ToArray();

        Console.WriteLine($"  выдача                   якорей {anchors.Length}, обёрнутых {wrapped.Length}");

        if (wrapped.Length > 0)
            await FollowAsync(page.Context, serp, wrapped[0]);

        await page.WaitForTimeoutAsync(12000);
        await BasicAsync(page);
    }

    /// <summary>Asks the engine where one wrapper leads, without following it.</summary>
    private static async Task FollowAsync(IBrowserContext context, string serp, string wrapper)
    {
        Console.WriteLine($"  пробуем                  {wrapper[..Math.Min(56, wrapper.Length)]}…");

        var reply = await context.APIRequest.GetAsync("https://www.google.com" + wrapper, new APIRequestContextOptions
        {
            MaxRedirects = 0,
            Headers = new Dictionary<string, string> { ["Referer"] = serp },
            Timeout = 30000
        });

        Console.WriteLine($"  ответ обёртки            {reply.Status} {reply.StatusText}");
        Console.WriteLine($"  Location                 {(reply.Headers.TryGetValue("location", out var found) ? found : "нет заголовка")}");
    }

    /// <summary>The arm kept as a record that the simplified layout is not a way out.</summary>
    private static async Task BasicAsync(IPage page)
    {
        var basic = $"https://www.google.com/search?q={Uri.EscapeDataString(Query)}&gbv=1";

        await page.GotoAsync(basic, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });
        await page.WaitForTimeoutAsync(2500);

        if (Serp.IsRefusal(page))
        {
            Console.WriteLine("  gbv=1                    ОТКАЗ /sorry/");

            return;
        }

        var anchors = await Serp.AnchorsAsync(page);
        var plain = anchors.Count(href => href?.StartsWith("http", StringComparison.Ordinal) == true);

        Console.WriteLine($"  gbv=1                    якорей {anchors.Length}, прямых {plain}");
        Console.WriteLine($"  адрес после gbv=1        {page.Url[..Math.Min(100, page.Url.Length)]}");
    }
}
