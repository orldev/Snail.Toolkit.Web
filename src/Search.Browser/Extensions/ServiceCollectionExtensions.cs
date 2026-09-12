using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping.Extensions;
using Snail.Toolkit.Web.Search.Extensions;

namespace Snail.Toolkit.Web.Search.Browser.Extensions;

/// <summary>
/// Registers the search providers that need a real browser.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers search with the browser-backed providers included.</summary>
        /// <remarks>
        /// Pulls in both halves deliberately: browser search is HTTP search plus a page reader, and a consumer
        /// that asked for it wants the HTTP providers alongside rather than instead. Both registrations are
        /// idempotent, so calling them again elsewhere costs nothing.
        /// </remarks>
        public IServiceCollection AddBrowserSearch(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddSearch(configuration);
            services.AddScraping(configuration);

            return services
                .AddGoogle(configuration)
                .AddBrave(configuration);
        }

        /// <summary>Adds the Google provider on top of a search and a page reader already registered.</summary>
        public IServiceCollection AddGoogle(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            Validated(services.AddOptions<GoogleOptions>()
                .Bind(configuration.GetSection(GoogleOptions.SectionName)));

            return services.AddSearchProvider<Google>();
        }

        /// <summary>Adds the Brave provider on top of a search and a page reader already registered.</summary>
        /// <remarks>
        /// Registered beside Google rather than instead of it, because which engine answers a given client
        /// from a given address is a fact that changes: measured on 2026-09-12 Brave answered and Google did
        /// not, and the aggregator is where that stops mattering. Each provider costs a page load, so a
        /// deployment that wants only one of them adds it on its own instead of taking this pair.
        /// </remarks>
        public IServiceCollection AddBrave(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            Validated(services.AddOptions<BraveOptions>()
                .Bind(configuration.GetSection(BraveOptions.SectionName)));

            return services.AddSearchProvider<Brave>();
        }
    }

    private static void Validated(OptionsBuilder<BraveOptions> builder)
        => builder
            .Validate(
                settings => IsHost(settings.Domain),
                $"'{BraveOptions.SectionName}:{nameof(BraveOptions.Domain)}' has to be a bare host such as 'search.brave.com', without a scheme or a path.")
            .ValidateOnStart();

    private static void Validated(OptionsBuilder<GoogleOptions> builder)
        => builder
            .Validate(
                settings => IsHost(settings.Domain),
                $"'{GoogleOptions.SectionName}:{nameof(GoogleOptions.Domain)}' has to be a bare host such as 'www.google.de', without a scheme or a path.")
            .ValidateOnStart();

    private static bool IsHost(string value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.Contains("://", StringComparison.Ordinal)
           && !value.Contains('/', StringComparison.Ordinal)
           && Uri.CheckHostName(value) is not UriHostNameType.Unknown;
}
