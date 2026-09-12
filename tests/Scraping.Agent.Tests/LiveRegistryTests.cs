using System.Text.Json;
using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Scraping.Agent.Extensions;
using Snail.Toolkit.Web.Scraping.Behavior;
using Xunit.Abstractions;

namespace Snail.Toolkit.Web.Scraping.Agent.Tests;

/// <summary>
/// Asks a live public registry for a record whose answer was known before this test was written.
/// </summary>
/// <remarks>
/// The rest of this suite drives miniatures of real pages served by nginx, which is what makes it hermetic and
/// also what leaves one claim untested: that any of it works against a site nobody here wrote. Two defects
/// measured on 2026-09-12 lived in exactly that gap and neither could have shown up against a miniature — the
/// block detector read the invisible reCAPTCHA token field as a refusal, and the platform's own label engine
/// did not resolve a caption that this registry binds to its field by position alone.
/// <para>
/// The expectation is a fact about the registry rather than about whether something came back: this vehicle
/// identifier was measured to hold two records in the federal half of it. A different count means the registry
/// changed, and the failure says which number it saw.
/// </para>
/// <para>
/// Skipped unless <see cref="LiveFactAttribute.Variable"/> names a browser server. Reaching a third party over
/// the public internet is not something a suite should do because somebody typed <c>dotnet test</c>.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Category", "Live")]
public class LiveRegistryTests(ITestOutputHelper output)
{
    private const string Page = "https://www.reestr-zalogov.ru/search/index";
    private const string Service = "/api/search/fedresurs";
    private const string Key = "6LdKJhMaAAAAAIfeHC6FZc-UVfzDQpiOjaJUWoxr";
    private const string Action = "search_fedresurs";
    private const string Vehicle = "WDC1660241B162435";
    private const string Company = "7707083893";
    private const int Measured = 2;

    /// <summary>
    /// The service answers its own page, and the answer carries the records that are there.
    /// </summary>
    /// <remarks>
    /// The same body sent over plain HTTP is refused outright: what the service checks — origin, cookies and a
    /// token the page minted for this exact route — belongs to the page and cannot be assembled from outside.
    /// The action is part of that contract rather than decoration, which cost an hour of blaming the browser
    /// before it was measured.
    /// </remarks>
    [LiveFact]
    public async Task SendAsync_ARegistryThatAnswersOnlyItsOwnPage_ReturnsTheRecordsItHolds()
    {
        await using var host = Host();

        var outcome = await host.Calls.SendAsync(new SiteCall(Link.Of(Page), Service, Asked())
        {
            Mint = new Mint(Key, Action),
            Settle = TimeSpan.FromSeconds(8)
        });

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());

        output.WriteLine(outcome.Value);

        using var answered = JsonDocument.Parse(outcome.Value);

        Assert.Equal(Measured, answered.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// The same service, asked about a company rather than a vehicle, answers with the charges against it.
    /// </summary>
    /// <remarks>
    /// A second route rather than a second assertion: the registry mints a token per endpoint and validates it
    /// against the route being called, so proving one says nothing about the other. The expectation is
    /// deliberately weaker than the vehicle one — a bank's pledge count moves week to week, and pinning an
    /// exact number here would fail for a reason that is not a defect, while "it answered with records at all"
    /// is the fact this check exists for.
    /// </remarks>
    [LiveFact]
    public async Task SendAsync_ACompanySearch_AnswersWithTheChargesAgainstIt()
    {
        await using var host = Host();

        var outcome = await host.Calls.SendAsync(new SiteCall(Link.Of(Page), Service, AskedAboutCompany())
        {
            Mint = new Mint(Key, Action),
            Settle = TimeSpan.FromSeconds(8)
        });

        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());

        output.WriteLine(outcome.Value);

        using var answered = JsonDocument.Parse(outcome.Value);

        Assert.True(answered.RootElement.GetProperty("total").GetInt32() > 0);
    }

    /// <summary>The company search body, keyed by tax number the way the registry's own form sends it.</summary>
    private static string AskedAboutCompany()
        => JsonSerializer.Serialize(new
        {
            mode = "onlyActual",
            limit = 10,
            offset = 0,
            filter = new
            {
                pledgor = new
                {
                    russianOrganization = new { name = "", ogrn = "", inn = Company }
                }
            }
        });

    /// <summary>The search body, built rather than pasted so its braces stay out of the way.</summary>
    private static string Asked()
        => JsonSerializer.Serialize(new
        {
            mode = "onlyActual",
            limit = 10,
            offset = 0,
            filter = new
            {
                property = new
                {
                    vehicleProperty = new { vin = Vehicle, pin = "", chassis = "", bodyNum = "" }
                }
            }
        });

    private Harness Host()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddXUnit(output).SetMinimumLevel(LogLevel.Debug));
        services.AddPageAgent(options =>
        {
            options.BrowserServer = LiveFactAttribute.Server;
            options.MaxRetryAttempts = 0;
            options.NavigationTimeoutMs = 60_000;
        });

        services.Configure<HumanBehaviorOptions>(options => options.Enabled = false);
        services.Configure<PolitenessOptions>(options => options.MinDelayPerHost = TimeSpan.Zero);

        return new Harness(services.BuildServiceProvider());
    }

    private sealed class Harness(ServiceProvider provider) : IAsyncDisposable
    {
        public ISiteCalls Calls { get; } = provider.GetRequiredService<ISiteCalls>();

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
