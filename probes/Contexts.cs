using System.Diagnostics;
using System.Globalization;
using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Probes;

/// <summary>
/// Measures what a browser context costs next to a browser process.
/// </summary>
/// <remarks>
/// The toolkit leases a whole browser process for the length of one visit, so the ceiling on pages in flight is
/// the number of processes it is willing to run. A context is an incognito profile inside a process and is
/// supposed to be far cheaper — but "supposed to be" is how a number gets invented, and the whole point of
/// raising the ceiling is choosing how many contexts one process may hand out. That number has to come from a
/// measurement or it is a guess wearing a setting's name.
/// <para>
/// The arms pair off: 1x4 against 4x1, 1x8 against 8x1. Each pair puts the same number of pages in flight and
/// changes one thing — whether they share a process. Anything else measured here would not tell the two apart.
/// </para>
/// <para>
/// Memory is read with <c>docker stats</c>, because the browser is in a container and the client cannot see its
/// resident set. When docker answers nothing the line says so rather than printing a zero: an unknown cost and
/// a free one are different facts, and this probe exists to tell them apart.
/// </para>
/// <para>
/// The page is built with <c>SetContentAsync</c> rather than fetched, so the probe touches no site and can be
/// run as often as it is doubted. What it costs a renderer — a document, a layout, a few thousand nodes — is
/// what a scraped page costs it too; what it does not include is the network stack for seventy subresources,
/// so these numbers are a floor rather than an average.
/// </para>
/// <para>
/// WHAT IT ANSWERED ON 2026-09-14, and the second answer is the one that mattered. Against stock Playwright
/// 1.62 running natively: a process with one page costs 284 MB, every further context on it costs 70, and
/// nothing degrades up to sixteen — opening a context takes 133 ms against 340 for a process, and the memory
/// comes back on close. Against our own ff-juggler image the two columns were identical, 178 MB a page however
/// the pages were arranged, and the process count explains why: it stays at ONE whatever the number of
/// connections. That image publishes an already-launched browser, so every connection attaches to the same
/// process, and the pool's <c>MaxBrowsers</c> counted connections while believing it counted browsers —
/// which is why that setting no longer exists.
/// </para>
/// <para>
/// The ff-juggler arm ran amd64 under emulation on an arm64 machine, so its absolute numbers — 178 MB, three
/// seconds to open a page — belong to that machine and not to a deployment. The process count does not: it is
/// a fact about the image.
/// </para>
/// </remarks>
internal static class Contexts
{
    /// <summary>The container the browser server runs in, named so its memory can be read.</summary>
    private static string Container =>
        Environment.GetEnvironmentVariable("SNAIL_PROBE_CONTAINER") is { Length: > 0 } named ? named : "pw-contexts";

    private static readonly (int Processes, int PerProcess)[] Arms =
    [
        (1, 1),
        (1, 4),
        (4, 1),
        (1, 8),
        (8, 1),
        (1, 16),
        (1, 1)
    ];

    /// <summary>A document with enough in it to make a renderer do renderer things.</summary>
    /// <remarks>
    /// Kept in <c>Pages/context.html</c>: its text is what a renderer lays out, in the language it was measured in.
    /// </remarks>
    private static readonly string Document = Samples.Page("context.html");

    public static async Task ReportAsync(string endpoint)
    {
        using var driver = await Playwright.CreateAsync();

        Console.WriteLine($"{DateTime.Now:HH:mm} — what a context costs beside a process");
        Console.WriteLine($"  container {Container}, server {endpoint}");

        await WarmedUpAsync(driver, endpoint);

        var idle = await MegabytesAsync();

        Console.WriteLine($"  server idle, after the warm-up:          {Printed(idle)}");
        Console.WriteLine();
        Console.WriteLine("  arm               pages    memory      per page   processes   opening: median / worst");

        foreach (var arm in Arms)
            await MeasureAsync(driver, endpoint, arm.Processes, arm.PerProcess);

        Console.WriteLine();
        Console.WriteLine($"  server idle again:                        {Printed(await MegabytesAsync())}");
        Console.WriteLine();
        Console.WriteLine("  The last arm repeats the first: the difference between them is drift,");
        Console.WriteLine("  and until it is small, the differences between the other arms cannot be read.");
        Console.WriteLine("  The two idle lines answer a question of their own: whether the memory of");
        Console.WriteLine("  closed contexts comes back, without which choosing their number means nothing.");
    }

    /// <summary>Opens and closes one page before anything is measured.</summary>
    /// <remarks>
    /// The first connection of a run pays for things no later one pays for — the browser binary read off disk,
    /// the first renderer started, the caches filled. Measured on 2026-09-14 without this: the opening arm
    /// reported 479 MB for one page and the same arm repeated at the end reported 285, so the control caught a
    /// cold start and every arm between them was read against the wrong baseline.
    /// </remarks>
    private static async Task WarmedUpAsync(IPlaywright driver, string endpoint)
    {
        await using var browser = await driver.Firefox.ConnectAsync(endpoint, new BrowserTypeConnectOptions { Timeout = 60000 });
        await using var context = await browser.NewContextAsync();

        var page = await context.NewPageAsync();

        await page.SetContentAsync(Document);
        await context.CloseAsync();
        await browser.CloseAsync();
        await Task.Delay(TimeSpan.FromSeconds(5));
    }

    private static string Printed(double? megabytes)
        => megabytes is { } number ? $"{number,7:F0} MB" : "unknown (docker did not answer)";

    private static async Task MeasureAsync(IPlaywright driver, string endpoint, int processes, int perProcess)
    {
        var before = await MegabytesAsync();
        var opening = new List<TimeSpan>();
        var browsers = new List<IBrowser>();
        var open = new List<IBrowserContext>();

        try
        {
            for (var process = 0; process < processes; process++)
                browsers.Add(await driver.Firefox.ConnectAsync(endpoint, new BrowserTypeConnectOptions { Timeout = 60000 }));

            foreach (var browser in browsers)
            {
                for (var index = 0; index < perProcess; index++)
                {
                    var started = Stopwatch.GetTimestamp();

                    var context = await browser.NewContextAsync(new BrowserNewContextOptions
                    {
                        Locale = "en-US",
                        ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
                    });

                    var page = await context.NewPageAsync();

                    await page.SetContentAsync(Document);

                    opening.Add(Stopwatch.GetElapsedTime(started));
                    open.Add(context);
                }
            }

            var after = await MegabytesAsync();
            var running = await FirefoxesAsync();

            Report(processes, perProcess, opening, before, after, running);
        }
        finally
        {
            foreach (var context in open)
                await context.CloseAsync();

            foreach (var browser in browsers)
                await browser.CloseAsync();

            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    private static void Report(
        int processes,
        int perProcess,
        List<TimeSpan> opening,
        double? before,
        double? after,
        int? running)
    {
        var pages = processes * perProcess;
        var sorted = opening.Order().ToArray();
        var median = sorted[sorted.Length / 2].TotalMilliseconds;
        var worst = sorted[^1].TotalMilliseconds;

        var cost = before is { } was && after is { } now
            ? $"{now - was,7:F0} MB  {(now - was) / pages,8:F0} MB"
            : "      —            —   ";

        var counted = running is { } number ? $"{number,6}" : "     ?";

        Console.WriteLine(
            $"  {processes,2} proc × {perProcess,2} ctx  {pages,8}  {cost}  {counted}      {median,7:F0} ms / {worst,5:F0} ms");
    }

    /// <summary>How many top-level browser processes the container is actually running.</summary>
    /// <remarks>
    /// The column that turns an inference into a measurement. A server published with
    /// <c>browserType.launchServer()</c> hands every connection the SAME already-launched browser, while
    /// <c>playwright run-server</c> launches a new one per connection — and the toolkit's pool, which counts
    /// connections and calls them browsers, cannot tell the two apart. Counting them in the container can.
    /// </remarks>
    private static async Task<int?> FirefoxesAsync()
    {
        var printed = await DockerAsync("exec", Container, "sh", "-c", "ps ax | grep -c '[f]irefox .*-profile'");

        return int.TryParse(printed?.Trim(), out var counted) ? counted : null;
    }

    /// <summary>The container's memory in megabytes, or nothing when docker does not answer.</summary>
    /// <remarks>
    /// <c>docker stats</c> prints a pair like <c>345.2MiB / 7.653GiB</c>; only the first half is this
    /// container's. A failure here is reported as an unknown rather than as a zero, because a probe that
    /// silently prints zeroes is worse than one that prints nothing.
    /// </remarks>
    private static async Task<double?> MegabytesAsync()
    {
        var printed = await DockerAsync("stats", "--no-stream", "--format", "{{.MemUsage}}", Container);

        return printed is null ? null : Megabytes(printed.Split('/')[0].Trim());
    }

    private static async Task<string?> DockerAsync(params string[] arguments)
    {
        try
        {
            var asked = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in arguments)
                asked.ArgumentList.Add(argument);

            using var docker = Process.Start(asked);

            if (docker is null)
                return null;

            var printed = await docker.StandardOutput.ReadToEndAsync();

            await docker.WaitForExitAsync();

            return docker.ExitCode == 0 ? printed : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double? Megabytes(string used)
    {
        var (suffix, scale) = used switch
        {
            _ when used.EndsWith("GiB", StringComparison.Ordinal) => ("GiB", 1024d),
            _ when used.EndsWith("MiB", StringComparison.Ordinal) => ("MiB", 1d),
            _ when used.EndsWith("KiB", StringComparison.Ordinal) => ("KiB", 1d / 1024),
            _ when used.EndsWith("B", StringComparison.Ordinal) => ("B", 1d / 1024 / 1024),
            _ => (string.Empty, 0d)
        };

        if (scale is 0)
            return null;

        return double.TryParse(used[..^suffix.Length], CultureInfo.InvariantCulture, out var number)
            ? number * scale
            : null;
    }
}
