using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Extensions;
using Snail.Toolkit.Web.Scraping.Extensions;

namespace Snail.Toolkit.Web.Scraping.Agent.Extensions;

/// <summary>
/// Registers the page agent and everything it drives.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the agent together with the scraping stack it walks pages with.</summary>
        public IServiceCollection AddPageAgent(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddScraping(configuration);

            Validated(services.BindOnce<AgentOptions>(configuration, AgentOptions.SectionName, static (options, section) => options.Bind(section)));

            services.TryAddSingleton<Moves>();
            services.TryAddSingleton<Setbacks>();
            services.TryAddSingleton<IPageAgent, PageAgent>();
            services.TryAddSingleton<ISiteCalls, SiteCalls>();

            return services.AddEngineAgent();
        }

        /// <summary>Registers the agent with the scraping stack configured in code rather than from settings.</summary>
        public IServiceCollection AddPageAgent(Action<ScrapingOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            services.AddScraping(configure);

            Validated(services.AddOptions<AgentOptions>());

            services.TryAddSingleton<Moves>();
            services.TryAddSingleton<Setbacks>();
            services.TryAddSingleton<IPageAgent, PageAgent>();
            services.TryAddSingleton<ISiteCalls, SiteCalls>();

            return services.AddEngineAgent();
        }
    }

    /// <summary>The agent a search engine walks its search box with, on attempts that hand a refusal back.</summary>
    private static IServiceCollection AddEngineAgent(this IServiceCollection services)
    {
        services.TryAddKeyedSingleton<IPageAgent>(
            EngineReading.Key,
            (provider, key) => ActivatorUtilities.CreateInstance<PageAgent>(
                provider,
                provider.GetRequiredKeyedService<IAttempts<PageContent>>(key)));

        return services;
    }

    private static void Validated(OptionsBuilder<AgentOptions> builder)
        => builder
            .Validate(
                settings => settings.Settle >= TimeSpan.Zero,
                $"'{AgentOptions.SectionName}:{nameof(AgentOptions.Settle)}' cannot be negative.")
            .ValidateOnStart();
}
