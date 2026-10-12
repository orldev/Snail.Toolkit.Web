using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Scraping.Behavior;
using Snail.Toolkit.Web.Scraping.Browsers;
using Snail.Toolkit.Web.Search.Browser.Extensions;
using PageOutcome = Snail.Toolkit.Web.Outcome<Snail.Toolkit.Web.Scraping.PageContent, Snail.Toolkit.Web.Scraping.PageFailure>;

namespace Snail.Toolkit.Web.Benchmarks;

/// <summary>The library as an application registers it, with the browser replaced by a page held in memory.</summary>
/// <remarks>
/// What a page costs once it has arrived is decided by the library's own wiring — which reader a crawl or an engine
/// is handed, and what that reader does with the markup — so the timings go through that wiring rather than through
/// the extraction calls one by one. Only the parts that would reach outside the process are replaced: the browser
/// visit answers the canned markup, the attempts run the read once under a fixed identity, and the site publishes
/// no sitemap.
/// </remarks>
public static class Browserless
{
    /// <summary>The key the library registers its search engines' readers under.</summary>
    private const string EngineKey = "Snail.Toolkit.Web.Search";

    /// <summary>Builds a container whose every page read answers <paramref name="html"/>.</summary>
    /// <param name="html">The markup every visit renders.</param>
    /// <returns>The container.</returns>
    public static ServiceProvider Serving(string html)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IVisits>(new CannedVisits(html));
        services.AddSingleton<IHumanBehavior, Still>();
        services.AddSingleton<ISiteIndex, Unlisted>();
        services.AddSingleton<IAttempts<PageContent>, Once>();
        services.AddKeyedSingleton<IAttempts<PageContent>, Once>(EngineKey);
        services.AddBrave(configuration);

        return services.BuildServiceProvider();
    }

    private static WebIdentity Identity { get; } = new()
    {
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        Platform = "Windows",
        NavigatorPlatform = "Win32",
        Brand = "Google Chrome",
        MajorVersion = "124",
        Locale = "en-US",
        Timezone = "America/New_York",
        AcceptLanguage = "en-US,en;q=0.9",
        Languages = ["en-US", "en"]
    };

    private sealed class Once : IAttempts<PageContent>
    {
        public Task<PageOutcome> AtAsync(
            Link address,
            Func<WebIdentity, Uri, CancellationToken, Task<PageOutcome>> work,
            CancellationToken ct = default)
            => work(Identity, address.Address!, ct);
    }

    private sealed class CannedVisits(string html) : IVisits
    {
        public Task<IVisit> OpenAsync(WebIdentity identity, Appetite appetite, Uri target, CancellationToken ct = default)
            => Task.FromResult<IVisit>(new CannedVisit(CannedPage.Of(html, target), identity));
    }

    private sealed class CannedVisit(IPage page, WebIdentity identity) : IVisit
    {
        public IPage Page { get; } = page;

        public WebIdentity Identity { get; } = identity;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Still : IHumanBehavior
    {
        public Task SimulateAsync(IPage page, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Unlisted : ISiteIndex
    {
        public Task<SiteListing> ListedAsync(CrawlScope scope, int room, CancellationToken ct = default)
            => Task.FromResult(SiteListing.None);
    }
}

/// <summary>A browser page that has already rendered the markup it was given.</summary>
/// <remarks>
/// A proxy rather than a class implementing the page, because the page's interface is Playwright's whole surface
/// and a read touches four members of it: the navigation, the markup, the rendered text and the address.
/// </remarks>
public class CannedPage : DispatchProxy
{
    private string _html = string.Empty;

    private string _address = string.Empty;

    /// <summary>Creates a page showing <paramref name="html"/> at <paramref name="address"/>.</summary>
    /// <param name="html">The markup.</param>
    /// <param name="address">The address the browser ended on.</param>
    /// <returns>The page.</returns>
    public static IPage Of(string html, Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var page = Create<IPage, CannedPage>();
        var canned = (CannedPage)(object)page;

        canned._html = html;
        canned._address = address.AbsoluteUri;

        return page;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        return targetMethod.Name switch
        {
            "get_Url" => _address,
            "ContentAsync" => Task.FromResult(_html),
            "EvaluateAsync" => Task.FromResult<string?>("The page as a reader sees it."),
            "GotoAsync" => Task.FromResult<IResponse?>(null),
            _ when targetMethod.ReturnType == typeof(Task) => Task.CompletedTask,
            _ => null
        };
    }
}
