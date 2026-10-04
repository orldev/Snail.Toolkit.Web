using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Extensions;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Scraping.Behavior;
using Snail.Toolkit.Web.Scraping.Browsers;
using Snail.Toolkit.Web.Scraping.Robots;
using Snail.Toolkit.Web.Scraping.Sitemaps;
using Snail.Toolkit.Web.Scraping.Stealth;
using Snail.Toolkit.Web.Sitemaps;

namespace Snail.Toolkit.Web.Scraping.Extensions;

/// <summary>
/// Registers the browser pool, the stealth layer and the two readers.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers scraping, binding every option class from configuration.</summary>
        /// <remarks>
        /// Calls the core's own registration first, so a consumer that only wants pages says one thing. The
        /// robots source is registered here rather than in the core, because reading <c>robots.txt</c> needs a
        /// transport and this package is the one that has one.
        /// </remarks>
        public IServiceCollection AddScraping(
            IConfiguration configuration,
            Action<ScrapingOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddWeb(configuration);

            var scraping = services.BindOnce<ScrapingOptions>(configuration, ScrapingOptions.SectionName);

            if (configure is not null)
                scraping.Configure(configure);

            Validated(scraping);

            Validated(services.BindOnce<HumanBehaviorOptions>(
                configuration, HumanBehaviorOptions.SectionName));

            Validated(services.BindOnce<CrawlOptions>(configuration, CrawlOptions.SectionName));

            return services.AddScrapingParts();
        }

        /// <summary>Registers scraping configured in code.</summary>
        public IServiceCollection AddScraping(Action<ScrapingOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            services.AddWeb();

            Validated(services.AddOptions<ScrapingOptions>().Configure(configure));
            Validated(services.AddOptions<HumanBehaviorOptions>());
            Validated(services.AddOptions<CrawlOptions>());

            return services.AddScrapingParts();
        }

        /// <summary>Registers the browser pool on its own, for a caller that drives Playwright directly.</summary>
        public IServiceCollection AddBrowserPool()
        {
            services.TryAddSingleton<IBrowserPool, BrowserPool>();

            return services;
        }

        private IServiceCollection AddScrapingParts()
        {
            services.AddBrowserPool();
            services.AddSiteRules<HttpRobotsSource>();

            services.TryAddSingleton<ISitemapSource, HttpSitemapSource>();
            services.TryAddSingleton<ISiteIndex, SitemapIndex>();
            services.TryAddSingleton<IStealthScript, StealthScript>();
            services.TryAddSingleton<IClientHints, ClientHints>();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IKeepsakes, Keepsakes>();
            services.TryAddSingleton<IVisits, Visits>();
            services.TryAddSingleton(typeof(IAttempts<>), typeof(Attempts<>));
            services.TryAddSingleton<IHumanBehavior, HumanBehavior>();
            services.TryAddSingleton<PageContents>();
            services.TryAddSingleton<IPageReader, PageReader>();
            services.TryAddSingleton<IPageSnapshots, PageSnapshots>();
            services.TryAddSingleton<ICrawler, Crawler>();

            services.TryAddKeyedSingleton<IAttempts<PageContent>>(
                EngineReading.Key,
                (provider, _) => new Attempts<PageContent>(
                    provider.GetRequiredService<IIdentityRotation>(),
                    provider.GetRequiredService<IPoliteness>(),
                    provider.GetRequiredService<ISiteRules>(),
                    provider.GetRequiredService<IOptions<ScrapingOptions>>(),
                    provider.GetRequiredService<IOptions<PolitenessOptions>>(),
                    provider.GetRequiredService<ILogger<Attempts<PageContent>>>(),
                    repeatsRefusals: false));

            services.TryAddKeyedSingleton<IPageReader>(
                EngineReading.Key,
                (provider, key) => ActivatorUtilities.CreateInstance<PageReader>(
                    provider,
                    provider.GetRequiredKeyedService<IAttempts<PageContent>>(key)));

            return services;
        }
    }

    private static void Validated(OptionsBuilder<ScrapingOptions> builder)
        => builder
            .Validate(
                settings => settings.MaxDegreeOfParallelism > 0,
                $"'{ScrapingOptions.SectionName}:{nameof(ScrapingOptions.MaxDegreeOfParallelism)}' has to be positive.")
            .Validate(
                settings => settings.NavigationTimeoutMs > 0,
                $"'{ScrapingOptions.SectionName}:{nameof(ScrapingOptions.NavigationTimeoutMs)}' has to be positive.")
            .Validate(
                settings => settings.MaxRetryAttempts >= 0,
                $"'{ScrapingOptions.SectionName}:{nameof(ScrapingOptions.MaxRetryAttempts)}' cannot be negative.")
            .Validate(
                settings => settings.RetryDelay >= TimeSpan.Zero,
                $"'{ScrapingOptions.SectionName}:{nameof(ScrapingOptions.RetryDelay)}' cannot be negative.")
            .Validate(
                settings => settings.MaxContentCharacters >= 0,
                $"'{ScrapingOptions.SectionName}:{nameof(ScrapingOptions.MaxContentCharacters)}' cannot be negative.")
            .Validate(
                settings => IsBrowserServer(settings.BrowserServer),
                $"'{ScrapingOptions.SectionName}:{nameof(ScrapingOptions.BrowserServer)}' has to be the ws:// or wss:// endpoint of a Playwright browser server, not a proxy address.")
            .Validate<IOptions<IdentityOptions>>(
                (settings, identity) => Announces(settings.Engine, identity.Value),
                $"'{ScrapingOptions.SectionName}:{nameof(ScrapingOptions.Engine)}' and '{IdentityOptions.SectionName}:{nameof(IdentityOptions.Fingerprints)}' name different browsers. Every configured user agent has to be one the chosen engine could have sent — a Gecko browser carrying a Chrome user agent is a contradiction any site reads in one line. The built-in fingerprints are Chrome, so an engine other than Chromium has to bring its own, such as 'Mozilla/5.0 (X11; Linux x86_64; rv:155.0) Gecko/20100101 Firefox/155.0'.")
            .ValidateOnStart();

    private static void Validated(OptionsBuilder<CrawlOptions> builder)
        => builder
            .Validate(
                settings => settings.MaxPages > 0,
                $"'{CrawlOptions.SectionName}:{nameof(CrawlOptions.MaxPages)}' has to be positive.")
            .Validate(
                settings => settings.Budget > TimeSpan.Zero,
                $"'{CrawlOptions.SectionName}:{nameof(CrawlOptions.Budget)}' has to be positive.")
            .Validate(
                settings => settings.MaxDepth >= 0,
                $"'{CrawlOptions.SectionName}:{nameof(CrawlOptions.MaxDepth)}' cannot be negative.")
            .Validate(
                settings => settings.MaxTextCharacters >= 0,
                $"'{CrawlOptions.SectionName}:{nameof(CrawlOptions.MaxTextCharacters)}' cannot be negative.")
            .Validate(
                settings => settings.MaxSitemaps > 0,
                $"'{CrawlOptions.SectionName}:{nameof(CrawlOptions.MaxSitemaps)}' has to be positive.")
            .Validate(
                settings => settings.SitemapTimeout > TimeSpan.Zero,
                $"'{CrawlOptions.SectionName}:{nameof(CrawlOptions.SitemapTimeout)}' has to be positive.")
            .Validate(
                settings => settings.MaxSitemapBytes > 0,
                $"'{CrawlOptions.SectionName}:{nameof(CrawlOptions.MaxSitemapBytes)}' has to be positive.")
            .ValidateOnStart();

    private static void Validated(OptionsBuilder<HumanBehaviorOptions> builder)
        => builder
            .Validate(
                settings => settings.MouseSteps > 0,
                $"'{HumanBehaviorOptions.SectionName}:{nameof(HumanBehaviorOptions.MouseSteps)}' has to be positive.")
            .Validate(
                settings => settings.HoverProbability is >= 0 and <= 1,
                $"'{HumanBehaviorOptions.SectionName}:{nameof(HumanBehaviorOptions.HoverProbability)}' has to be between 0 and 1.")
            .Validate(
                settings => settings.MinPauseMs >= 0 && settings.MaxPauseMs >= settings.MinPauseMs,
                $"'{HumanBehaviorOptions.SectionName}:{nameof(HumanBehaviorOptions.MaxPauseMs)}' has to be at least '{nameof(HumanBehaviorOptions.MinPauseMs)}'.")
            .Validate(
                settings => settings.MaxScrollDistance > 0,
                $"'{HumanBehaviorOptions.SectionName}:{nameof(HumanBehaviorOptions.MaxScrollDistance)}' has to be positive.")
            .Validate(
                settings => settings.Budget > TimeSpan.Zero,
                $"'{HumanBehaviorOptions.SectionName}:{nameof(HumanBehaviorOptions.Budget)}' has to be positive.")
            .ValidateOnStart();

    private static bool IsBrowserServer(string? value)
        => string.IsNullOrWhiteSpace(value)
           || (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "ws" or "wss");

    private static bool Announces(Engine engine, IdentityOptions identity)
    {
        string[] agents =
        [
            .. identity.Fingerprints
                .Select(fingerprint => fingerprint.UserAgent)
                .Where(agent => !string.IsNullOrWhiteSpace(agent))
        ];

        return agents.Length == 0
            ? engine is Engine.Chromium
            : Array.TrueForAll(agents, agent => Announces(engine, agent));
    }

    private static bool Announces(Engine engine, string userAgent) => engine switch
    {
        Engine.Firefox => userAgent.Contains("Firefox/", StringComparison.Ordinal),
        Engine.WebKit => !userAgent.Contains("Chrome/", StringComparison.Ordinal)
                         && !userAgent.Contains("Firefox/", StringComparison.Ordinal),
        _ => userAgent.Contains("Chrome/", StringComparison.Ordinal)
    };
}
