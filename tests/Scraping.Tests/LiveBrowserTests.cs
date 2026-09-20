using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Browsers;
using Snail.Toolkit.Web.Scraping.Extensions;
using Snail.Toolkit.Web.Tests;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Asks the configured browser server what it is, before anything starts blaming the sites it visits.
/// </summary>
/// <remarks>
/// Every measurement in <c>browser/PATCHES.md</c> rests on one claim that nothing else here checks: the server
/// named in the environment serves the patched build, whose <c>navigator.webdriver</c> is <c>false</c> because
/// the C++ was changed, not because a script said so. Point <see cref="LiveFactAttribute.Variable"/> at a
/// stock Playwright server — a typo is enough — and the live engine suites turn red together, every failure
/// reading as a layout change at an engine that is in fact answering a self-declared robot exactly as it
/// should. This says so in one line instead.
/// <para>
/// The three states are not two. Measured on 2026-09-13, a stock build was answered <c>/sorry/</c> by Google,
/// and so was a stock build whose <c>webdriver</c> a script had rewritten to <c>false</c>; only the patched
/// build was answered with results. The value alone therefore proves nothing, and the getter's own source is
/// what separates them: a forged one hands back its source, a real one hands back <c>[native code]</c>.
/// </para>
/// <para>
/// Two documents, because the two questions are not the same question. What the server reports on its own is
/// read from a bare context, with nothing of ours in it — through a dressed one the answer would be worthless,
/// since the stealth script rewrites a <c>webdriver</c> that says <c>true</c> and so makes a stock server
/// report exactly what the patched one reports. That was measured here on 2026-09-14 and is the reason this
/// class opens a context of its own rather than reading everything off the visit.
/// </para>
/// <para>
/// The environment condition of <c>PATCHES.md</c> — a time zone that is not <c>UTC</c> — is deliberately left
/// out. The context sets <c>TimezoneId</c> from the identity, so the page reports what this test configured
/// and would agree with itself whatever the container runs.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Category", "Live")]
public class LiveBrowserTests
{
    private const string Gecko =
        "Mozilla/5.0 (X11; Linux x86_64; rv:155.0) Gecko/20100101 Firefox/155.0";

    private const string Native = "[native code]";

    /// <summary>Reads what a document says about its own navigator, and about who last touched it.</summary>
    private const string Probe =
        """
        () => {
            const sourceOf = (name) => {
                const descriptor = Object.getOwnPropertyDescriptor(Navigator.prototype, name);
                return descriptor && descriptor.get ? descriptor.get.toString() : '';
            };

            return {
                webdriver: navigator.webdriver,
                owned: Object.hasOwn(navigator, 'webdriver'),
                webdriverGetter: sourceOf('webdriver'),
                concurrencyGetter: sourceOf('hardwareConcurrency')
            };
        }
        """;

    private static readonly Lazy<Task<Reading>> Measured = new(MeasureAsync);

    /// <summary>The patched line itself, read where nothing of ours can have written it.</summary>
    /// <remarks>
    /// This is the check the class exists for, and the only one that tells a wrong server from a broken one.
    /// </remarks>
    [LiveFact]
    public async Task RentAsync_TheConfiguredServer_ReportsWebdriverFalseOfItself()
    {
        var reading = await Measured.Value;

        Assert.False(
            reading.Bare.Webdriver,
            $"{LiveFactAttribute.Variable} names a browser that declares itself automated, so it is not the "
            + "patched build. Every live engine check will fail after this one, and none of them will say why.");
    }

    /// <summary>And it is the browser saying so, not something injected into the image.</summary>
    /// <remarks>
    /// A <c>false</c> arriving from a script baked into the image would pass the check above and be worth
    /// nothing: both spoofed arms of the 2026-09-13 table reported exactly that value and were answered
    /// <c>/sorry/</c> all the same. <c>Object.defineProperty</c> leaves a getter whose <c>toString()</c> is its
    /// own source, which is read in one line by anyone who bothers.
    /// </remarks>
    [LiveFact]
    public async Task RentAsync_TheServersWebdriverGetter_IsTheBrowsersOwn()
    {
        var reading = await Measured.Value;

        Assert.Contains(Native, reading.Bare.WebdriverGetter, StringComparison.Ordinal);
    }

    /// <summary>The toolkit then leaves that getter alone, which it once did not.</summary>
    /// <remarks>
    /// The regression that cost a day and a half: the stealth script redefined <c>webdriver</c> on every
    /// visit, undoing the build it was running on and putting the browser back into the second row of the
    /// table in section 1 of <c>PATCHES.md</c>. The rule it became — every patch asks the page rather than the
    /// configuration — has no other guard.
    /// </remarks>
    [LiveFact]
    public async Task OpenAsync_TheStealthScript_LeavesTheWebdriverGetterAsItFoundIt()
    {
        var reading = await Measured.Value;

        Assert.Contains(Native, reading.Dressed.WebdriverGetter, StringComparison.Ordinal);
    }

    /// <summary>Nor has it defined <c>webdriver</c> on the navigator instance, the other place one hides.</summary>
    /// <remarks>
    /// An own property on the instance shadows the prototype, so the value would read <c>false</c> while the
    /// prototype getter stayed native and the check above passed over the forgery. This is the shape most
    /// stealth plugins take, and the shape the toolkit's own script would take if the browser ever stopped
    /// exposing <c>webdriver</c> on the prototype.
    /// </remarks>
    [LiveFact]
    public async Task OpenAsync_Webdriver_IsNotRedefinedOnTheNavigatorItself()
    {
        var reading = await Measured.Value;

        Assert.False(
            reading.Dressed.IsOwnedByNavigator,
            "Something defined 'webdriver' on the navigator instance, where it shadows the prototype the "
            + $"check above reads: {reading.Dressed.WebdriverGetter}");
    }

    /// <summary>The stealth script did run on the document those two were read from.</summary>
    /// <remarks>
    /// Without this the pair above would pass against a document no init script ever reached, where every
    /// property is native for the dullest of reasons. <c>hardwareConcurrency</c> is the witness because the
    /// script rewrites it unconditionally — while the value it writes, eight, is also what the build reports
    /// on its own, so the value proves nothing here and the getter's source proves everything.
    /// </remarks>
    [LiveFact]
    public async Task OpenAsync_TheStealthScript_RanOnTheDocumentTheOthersWereReadFrom()
    {
        var reading = await Measured.Value;

        Assert.DoesNotContain(Native, reading.Dressed.ConcurrencyGetter, StringComparison.Ordinal);
    }

    /// <summary>Reads the server bare, then reads it again dressed as a visit dresses it.</summary>
    /// <remarks>
    /// The bare context is taken first and released before the visit is opened: the pool defaults to one
    /// browser, and holding a lease across the visit would wait for a browser that only this method can give
    /// back.
    /// </remarks>
    private static async Task<Reading> MeasureAsync()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddScraping(options =>
        {
            options.BrowserServer = LiveFactAttribute.Server;
            options.Engine = Engine.Firefox;
            options.MaxRetryAttempts = 0;
            options.NavigationTimeoutMs = 30_000;
        });

        services.Configure<IdentityOptions>(
            options => options.Fingerprints = [new Fingerprint { UserAgent = Gecko }]);

        await using var provider = services.BuildServiceProvider();

        return new Reading(
            await BareAsync(provider.GetRequiredService<IBrowserPool>()),
            await DressedAsync(provider));
    }

    /// <summary>What the server reports with nothing of ours on the page.</summary>
    private static async Task<Sighting> BareAsync(IBrowserPool pool)
    {
        await using var lease = await pool.RentAsync();
        await using var context = await lease.Browser.NewContextAsync();

        return await SeenAsync(await context.NewPageAsync());
    }

    /// <summary>What it reports through the page a read of any site would have been given.</summary>
    /// <remarks>
    /// Through <see cref="IVisits"/> rather than raw Playwright, because half the claim is about what the
    /// toolkit does to the browser afterwards. <c>about:blank</c> is navigated to rather than merely
    /// inhabited: the init script is registered on the context before the page exists, and an explicit
    /// navigation leaves no question about which documents it reached.
    /// </remarks>
    private static async Task<Sighting> DressedAsync(IServiceProvider provider)
    {
        var identity = provider.GetRequiredService<IIdentityRotation>().Next();

        await using var visit = await provider.GetRequiredService<IVisits>()
            .OpenAsync(identity, Appetite.Everything, new Uri("https://example.com/"));

        return await SeenAsync(visit.Page);
    }

    private static async Task<Sighting> SeenAsync(IPage page)
    {
        await page.GotoAsync("about:blank");

        var read = await page.EvaluateAsync<JsonElement>(Probe);

        return new Sighting(
            read.GetProperty("webdriver").GetBoolean(),
            read.GetProperty("owned").GetBoolean(),
            read.GetProperty("webdriverGetter").GetString() ?? string.Empty,
            read.GetProperty("concurrencyGetter").GetString() ?? string.Empty);
    }

    /// <summary>The server seen twice: as it is, and as a visit leaves it.</summary>
    private sealed record Reading(Sighting Bare, Sighting Dressed);

    /// <summary>What one document said about its own navigator.</summary>
    private sealed record Sighting(
        bool Webdriver,
        bool IsOwnedByNavigator,
        string WebdriverGetter,
        string ConcurrencyGetter);
}
