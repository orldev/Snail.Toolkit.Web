using System.Text.Json;
using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Probes;

/// <summary>
/// Reports the network fingerprint a build presents: JA3/JA4, the HTTP/2 settings and the header order.
/// </summary>
/// <remarks>
/// Written to answer whether a WAF sees a Firefox signature over a script's handshake. For a plain HTTP client
/// that is literally true — curl offers an OpenSSL hello under whatever user agent it is told to send. For a
/// build made from Firefox sources it is the open question, and the only way to close it is to look.
/// <para>
/// Two arms, one axis — the build: ours through the browser server, and stock Playwright Firefox started
/// locally. Matching fingerprints mean the patch never touched the network stack. This asks a mirror rather
/// than a search engine, so it burns nobody's patience and can be repeated as often as it is useful.
/// </para>
/// <para>
/// Read the JA4 in two halves. The digits after <c>t13d</c> count cipher suites and extensions, so
/// <c>1517</c> against <c>1617</c> is one cipher suite of difference and not a different client. The expanded
/// form says which. A difference there is worth chasing only after checking the two builds are the same
/// Firefox version: on 2026-09-14 a missing legacy CBC cipher turned out to be 155 against 153.
/// </para>
/// </remarks>
internal static class Fingerprint
{
    private const string Mirror = "https://tls.peet.ws/api/all";

    public static async Task ReportAsync(string endpoint)
    {
        using var driver = await Playwright.CreateAsync();

        Console.WriteLine($"{DateTime.Now:HH:mm} — сетевой отпечаток");

        await using (var ours = await driver.Firefox.ConnectAsync(endpoint, new BrowserTypeConnectOptions { Timeout = 30000 }))
            await AskAsync(ours, "наша сборка (сервер)");

        await using (var stock = await driver.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }))
            await AskAsync(stock, "штатный Playwright-Firefox (локально)");
    }

    private static async Task AskAsync(IBrowser browser, string label)
    {
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "en-US",
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Accept-Language"] = "en-US,en;q=0.9" }
        });

        var page = await context.NewPageAsync();

        await page.GotoAsync(Mirror, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });

        var body = await page.EvaluateAsync<string>("() => document.body.innerText");

        Console.WriteLine();
        Console.WriteLine($"  === {label} ===");

        try
        {
            using var seen = JsonDocument.Parse(body);

            Report(seen.RootElement);
        }
        catch (JsonException)
        {
            Console.WriteLine($"  зеркало ответило не JSON: {body[..Math.Min(200, body.Length)]}");
        }
    }

    private static void Report(JsonElement root)
    {
        if (root.TryGetProperty("tls", out var tls))
        {
            Show(tls, "ja3_hash", "  JA3");
            Show(tls, "ja4", "  JA4");
            Show(tls, "ja4_r", "  JA4 развёрнутый");
            Show(tls, "peetprint_hash", "  peetprint");
        }

        if (root.TryGetProperty("http2", out var http2) && http2.TryGetProperty("akamai_fingerprint_hash", out var akamai))
            Console.WriteLine($"  HTTP/2                  {akamai.GetString()}");

        Show(root, "http_version", "  версия");

        if (root.TryGetProperty("http1", out var http1) && http1.TryGetProperty("headers", out var plain))
            foreach (var header in plain.EnumerateArray())
                Console.WriteLine($"    {header.GetString()}");

        if (!root.TryGetProperty("http2", out var two) || !two.TryGetProperty("sent_frames", out var frames))
            return;

        foreach (var frame in frames.EnumerateArray())
        {
            if (!frame.TryGetProperty("headers", out var list))
                continue;

            foreach (var header in list.EnumerateArray())
                Console.WriteLine($"    {header.GetString()}");
        }
    }

    private static void Show(JsonElement root, string name, string label)
    {
        if (root.TryGetProperty(name, out var value) && value.GetString() is { } text)
            Console.WriteLine($"{label,-25} {text}");
    }
}
