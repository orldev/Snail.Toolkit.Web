using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Extensions;
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
        /// <para>
        /// Google is not here. It answers a form rather than an address — measured on 2026-09-13, its results
        /// address returns <c>/sorry/</c> where its own form returns results — and walking a form needs the
        /// page agent, which this package deliberately does not depend on. It ships in
        /// <c>Snail.Toolkit.Web.Search.Browser.Agent</c>, and a deployment that wants both adds that one too.
        /// </para>
        /// </remarks>
        public IServiceCollection AddBrowserSearch(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddBrave(configuration);

            return services.AddBing(configuration);
        }

        /// <summary>Adds Bing, which answers a browser and not a plain request.</summary>
        /// <remarks>
        /// THE ONLY READING OF THIS ENGINE THERE IS, since 2026-09-20. It used to take the place of an HTTP
        /// provider of the same name, and this method carried the code that removed that one from the
        /// registrations; the owner had it deleted instead, so there is nothing left to displace.
        /// <para>
        /// Why it was deleted rather than kept for deployments without a browser: measured on 2026-09-15 from
        /// a datacentre address, the HTTP reading answered four Russian queries out of five with ten confident
        /// results about something else — bible sites for «Rusnano» (in Russian), a hotel chain, Zurich tourism, car parts —
        /// while the same address read through a browser answered with the company, its Wikipedia article and
        /// the news about its licence. A provider that keeps the shape of an answer while saying nothing is
        /// worse than a provider that is absent: absence is visible in the report.
        /// </para>
        /// </remarks>
        public IServiceCollection AddBing(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddSearch(configuration);
            services.AddScraping(configuration);

            Validated(services.BindOnce<BingOptions>(configuration, BingOptions.SectionName, static (options, section) => options.Bind(section)));

            return services.AddSearchProvider<Bing>();
        }

        /// <summary>Adds Brave, read at its own results address.</summary>
        /// <remarks>
        /// Registered beside Google rather than instead of it, because which engine answers a given client
        /// from a given address is a fact that changes: measured on 2026-09-12 Brave answered and Google did
        /// not, and on 2026-09-14 Brave answered <c>429</c> from the same address while Google returned six
        /// results. The aggregator is where that stops mattering. Each provider costs a page load, so a
        /// deployment that wants only one of them adds it on its own instead of taking the set.
        /// <para>
        /// Pulls in search and the page reader rather than assuming them, so that saying one thing is enough —
        /// the same way <c>AddGoogle</c> does in the package next door. Until 2026-09-14 this method registered
        /// the provider alone, so a deployment that took the remark above at its word built a container that
        /// resolved <see cref="Brave"/> into an <c>IPageReader</c> nobody had registered. That failure arrives
        /// on the first query rather than at startup, because validation checks settings and not the graph.
        /// </para>
        /// </remarks>
        public IServiceCollection AddBrave(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddSearch(configuration);
            services.AddScraping(configuration);

            Validated(services.BindOnce<BraveOptions>(configuration, BraveOptions.SectionName, static (options, section) => options.Bind(section)));

            return services.AddSearchProvider<Brave>();
        }
    }

    private static void Validated(OptionsBuilder<BingOptions> builder)
        => builder
            .Validate(
                settings => IsHost(settings.Domain),
                $"'{BingOptions.SectionName}:{nameof(BingOptions.Domain)}' has to be a bare host such as 'www.bing.com', without a scheme or a path.")
            .ValidateOnStart();

    private static void Validated(OptionsBuilder<BraveOptions> builder)
        => builder
            .Validate(
                settings => IsHost(settings.Domain),
                $"'{BraveOptions.SectionName}:{nameof(BraveOptions.Domain)}' has to be a bare host such as 'search.brave.com', without a scheme or a path.")
            .ValidateOnStart();

    private static bool IsHost(string value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.Contains("://", StringComparison.Ordinal)
           && !value.Contains('/', StringComparison.Ordinal)
           && Uri.CheckHostName(value) is not UriHostNameType.Unknown;
}
