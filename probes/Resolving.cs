using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Probes;

/// <summary>
/// Measures what resolving a wrapped link costs: whether it needs the session, and how fast it may be repeated.
/// </summary>
/// <remarks>
/// Two questions, and both decide the shape of the code rather than a setting. If a client with no cookies,
/// no referer and no session is answered the same way, resolution does not belong in the browser at all and is
/// an ordinary request in the provider — which is what was measured on 2026-09-14 and why it is written that
/// way now. If wrappers can be followed in turn at a short interval, one query costs one page plus one small
/// request per link instead of a wait per link.
/// <para>
/// The blobs are taken FRESH from this very page. They are bound to the results page that issued them, and on
/// a stale one there is no way to tell "expired" from "needs the session" — an axis that looks like one
/// measurement and is two.
/// </para>
/// </remarks>
internal static class Resolving
{
    private const string Query = "postgresql documentation";

    public static async Task ReportAsync(string endpoint)
    {
        using var driver = await Playwright.CreateAsync();
        await using var browser = await driver.Firefox.ConnectAsync(endpoint, new BrowserTypeConnectOptions { Timeout = 30000 });

        Console.WriteLine($"{DateTime.Now:HH:mm} — чем платит раскрытие");

        var page = await Serp.OpenAsync(browser, Query);

        if (Serp.IsRefusal(page))
        {
            Console.WriteLine("  выдача                   ОТКАЗ /sorry/ — замер не состоялся");

            return;
        }

        var serp = page.Url;
        var wrapped = (await Serp.AnchorsAsync(page))
            .Where(href => href?.StartsWith('/') == true)
            .Select(href => "https://www.google.com" + href)
            .ToArray();

        Console.WriteLine($"  обёрнутых                {wrapped.Length}");

        if (wrapped.Length == 0)
            return;

        await SessionAsync(page.Context, serp, wrapped[0]);
        await InTurnAsync(page.Context, serp, wrapped);
    }

    /// <summary>One blob, two clients: the axis is the session and nothing else.</summary>
    private static async Task SessionAsync(IBrowserContext context, string serp, string wrapper)
    {
        var browsed = await context.APIRequest.GetAsync(wrapper, new APIRequestContextOptions
        {
            MaxRedirects = 0,
            Headers = new Dictionary<string, string> { ["Referer"] = serp },
            Timeout = 30000
        });

        Console.WriteLine($"  из браузера              {browsed.Status} → {(browsed.Headers.TryGetValue("location", out var led) ? led : "нет Location")}");

        using var bare = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        try
        {
            using var raw = await bare.GetAsync(wrapper);

            Console.WriteLine($"  голым клиентом           {(int)raw.StatusCode} → {raw.Headers.Location?.ToString() ?? "нет Location"}");
        }
        catch (HttpRequestException failure)
        {
            Console.WriteLine($"  голым клиентом           сорвалось: {failure.Message}");
        }
    }

    /// <summary>Every wrapper in turn, at the interval the provider uses.</summary>
    private static async Task InTurnAsync(IBrowserContext context, string serp, string[] wrapped)
    {
        Console.WriteLine("  подряд, шаг 1.5 с:");

        var led = 0;

        foreach (var (wrapper, index) in wrapped.Select((value, index) => (value, index)))
        {
            var reply = await context.APIRequest.GetAsync(wrapper, new APIRequestContextOptions
            {
                MaxRedirects = 0,
                Headers = new Dictionary<string, string> { ["Referer"] = serp },
                Timeout = 30000
            });

            var found = reply.Headers.TryGetValue("location", out var location) ? location : null;

            if (reply.Status == 302 && found is not null)
                led++;

            var shown = found ?? "нет Location";

            Console.WriteLine($"    {index + 1}. {reply.Status} {shown[..Math.Min(70, shown.Length)]}");

            await Task.Delay(1500);
        }

        Console.WriteLine($"  раскрылось               {led} из {wrapped.Length}");
    }
}
