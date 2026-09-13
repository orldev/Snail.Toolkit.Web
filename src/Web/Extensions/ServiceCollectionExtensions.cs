using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Extensions;

/// <summary>
/// Registers the shared behaviour both engines stand on.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Binds the toolkit's configuration sections and registers everything the core provides.</summary>
        /// <remarks>
        /// Robots rules are absent on purpose: obeying them means downloading <c>robots.txt</c>, and the core
        /// owns no transport. Whichever engine is added next registers its own source with
        /// <see cref="AddSiteRules{TSource}"/>.
        /// </remarks>
        public IServiceCollection AddWeb(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            services.BindOnce<IdentityOptions>(configuration, IdentityOptions.SectionName);
            services.BindOnce<PolitenessOptions>(configuration, PolitenessOptions.SectionName);
            services.BindOnce<RobotsOptions>(configuration, RobotsOptions.SectionName);

            return services.AddWeb();
        }

        /// <summary>Binds a settings section the first time it is asked for, and does nothing after that.</summary>
        /// <remarks>
        /// <c>TryAdd</c> guards the services; nothing guarded the settings. Binding appends to a
        /// <see cref="List{T}"/> rather than replacing it, so every module that reaches the core's own section
        /// added the configured entries again: measured on 2026-09-13, a consumer calling
        /// <c>AddPageAgent</c> beside <c>AddBrowserSearch</c> got one configured proxy three times and passed
        /// <c>--lang=en-US</c> to Chromium twice.
        /// <para>
        /// A marker per settings type is what makes the second call free. <see cref="ServiceCollectionDescriptorExtensions"/>
        /// deduplicates only descriptors that name an implementation type, and a bound section is an instance,
        /// so it can never be one of those.
        /// </para>
        /// </remarks>
        public OptionsBuilder<TOptions> BindOnce<TOptions>(IConfiguration configuration, string section)
            where TOptions : class
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var builder = services.AddOptions<TOptions>();

            if (services.Any(registration => registration.ServiceType == typeof(Bound<TOptions>)))
                return builder;

            services.AddSingleton(new Bound<TOptions>());

            return builder.Bind(configuration.GetSection(section));
        }

        /// <summary>Registers everything the core provides, leaving the settings at their defaults.</summary>
        /// <remarks>
        /// For a consumer that configures in code rather than from a file. Validation is registered here rather
        /// than beside the binding, so both routes refuse the same impossible settings.
        /// </remarks>
        public IServiceCollection AddWeb()
        {
            Validated(services.AddOptions<IdentityOptions>());
            Validated(services.AddOptions<PolitenessOptions>());
            Validated(services.AddOptions<RobotsOptions>());

            return services
                .AddIdentityRotation()
                .AddPoliteness()
                .AddBlockDetection()
                .AddTextExtraction()
                .AddLinkExtraction();
        }

        /// <summary>Registers one identity and proxy rotation shared by every caller.</summary>
        public IServiceCollection AddIdentityRotation()
        {
            services.TryAddSingleton<IProxyRotation, ProxyRotation>();
            services.TryAddSingleton<IIdentityRotation, IdentityRotation>();

            return services;
        }

        /// <summary>Registers the per-host gate.</summary>
        /// <remarks>Singleton because a gate is only a limit while every caller waits at the same one.</remarks>
        public IServiceCollection AddPoliteness()
        {
            services.TryAddSingleton<IPoliteness, HostPoliteness>();

            return services;
        }

        /// <summary>Registers challenge and refusal detection.</summary>
        public IServiceCollection AddBlockDetection()
        {
            services.TryAddSingleton<IBlockDetector, BlockDetector>();

            return services;
        }

        /// <summary>Registers readable-text extraction.</summary>
        public IServiceCollection AddTextExtraction()
        {
            services.TryAddSingleton<ITextExtraction, TextExtraction>();

            return services;
        }

        /// <summary>Registers link extraction.</summary>
        public IServiceCollection AddLinkExtraction()
        {
            services.TryAddSingleton<ILinkExtraction, LinkExtraction>();

            return services;
        }

        /// <summary>Registers robots rules on top of the transport that can read them.</summary>
        public IServiceCollection AddSiteRules<TSource>()
            where TSource : class, IRobotsSource
        {
            services.TryAddSingleton<IRobotsSource, TSource>();
            services.TryAddSingleton<ISiteRules, SiteRules>();

            return services;
        }
    }

    /// <summary>Records that one settings type has had its section bound already.</summary>
    private sealed class Bound<TOptions>
        where TOptions : class;

    /// <summary>Refuses to start the host on settings that cannot work.</summary>
    /// <remarks>
    /// A malformed proxy used to throw <see cref="FormatException"/> from the middle of a fetch, minutes into a
    /// crawl and nowhere near the configuration that caused it. Validation on start moves that to the one moment
    /// where the message is unambiguous.
    /// </remarks>
    private static void Validated(OptionsBuilder<IdentityOptions> builder)
        => builder
            .Validate(
                settings => settings.Proxies.TrueForAll(entry => ProxyEndpoint.TryParse(entry, out _)),
                $"'{IdentityOptions.SectionName}:{nameof(IdentityOptions.Proxies)}' holds an entry that is not '[scheme://][user:password@]host:port'.")
            .ValidateOnStart();

    private static void Validated(OptionsBuilder<PolitenessOptions> builder)
        => builder
            .Validate(
                settings => settings.MinDelayPerHost >= TimeSpan.Zero,
                $"'{PolitenessOptions.SectionName}:{nameof(PolitenessOptions.MinDelayPerHost)}' cannot be negative.")
            .Validate(
                settings => settings.MaxConcurrentPerHost > 0,
                $"'{PolitenessOptions.SectionName}:{nameof(PolitenessOptions.MaxConcurrentPerHost)}' has to be positive.")
            .Validate(
                settings => settings.HostDelays.Values.All(gap => gap >= TimeSpan.Zero),
                $"'{PolitenessOptions.SectionName}:{nameof(PolitenessOptions.HostDelays)}' holds a negative gap.")
            .Validate(
                settings => settings.HostDelays.Keys.All(host => !string.IsNullOrWhiteSpace(host))
                    && settings.HostDelays.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == settings.HostDelays.Count,
                $"'{PolitenessOptions.SectionName}:{nameof(PolitenessOptions.HostDelays)}' names a host twice, or names an empty one.")
            .ValidateOnStart();

    private static void Validated(OptionsBuilder<RobotsOptions> builder)
        => builder
            .Validate(
                settings => settings.Timeout > TimeSpan.Zero,
                $"'{RobotsOptions.SectionName}:{nameof(RobotsOptions.Timeout)}' has to be positive.")
            .Validate(
                settings => settings.CacheLifetime > TimeSpan.Zero,
                $"'{RobotsOptions.SectionName}:{nameof(RobotsOptions.CacheLifetime)}' has to be positive.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.UserAgent),
                $"'{RobotsOptions.SectionName}:{nameof(RobotsOptions.UserAgent)}' is required.")
            .ValidateOnStart();
}
