using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Extensions;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Scraping.Agent.Extensions;
using Snail.Toolkit.Web.Search.Extensions;

namespace Snail.Toolkit.Web.Search.Browser.Agent.Extensions;

/// <summary>
/// Registers the search providers that have to be walked through their own page.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds Google, walked through its own form by the page agent.</summary>
        /// <remarks>
        /// Pulls in search and the agent rather than assuming them, so a consumer that wants this one engine
        /// says one thing. Both registrations are idempotent, and a deployment that also wants the link-driven
        /// engines adds <c>AddBrowserSearch</c> beside this — the aggregator asks every provider it is given.
        /// </remarks>
        public IServiceCollection AddGoogle(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddSearch(configuration);
            services.AddPageAgent(configuration);

            Validated(services.BindOnce<GoogleOptions>(configuration, GoogleOptions.SectionName));

            services.Paced();
            services.Following();

            return services.AddSearchProvider<Google>();
        }
    }

    /// <summary>Gives this engine a gap measured in minutes unless the deployment named its own.</summary>
    /// <remarks>
    /// The default gap is one second, which is a rate this engine answers with a challenge. Measured on
    /// 2026-09-13 from one address: four queries spaced two to four minutes apart, each on a clean context,
    /// were answered with results, and the shortest interval that passed was two minutes. Faster was never
    /// measured to work, so two minutes is the floor written here rather than a rate known to be safe — a
    /// deployment that asks more often should expect the refusal that frequency buys.
    /// <para>
    /// Named after the configured front end rather than hard-coded, so a country domain is paced too, and
    /// only when the deployment has not set its own gap for that host.
    /// </para>
    /// </remarks>
    private static void Paced(this IServiceCollection services)
        => services
            .AddOptions<PolitenessOptions>()
            .PostConfigure<IOptions<GoogleOptions>>((politeness, google) =>
            {
                if (!politeness.HostDelays.ContainsKey(google.Value.Domain))
                    politeness.HostDelays[google.Value.Domain] = TimeSpan.FromMinutes(2);
            });

    /// <summary>Adds the client that asks where a wrapped link leads without going there.</summary>
    /// <remarks>
    /// Redirects are off because the redirect is the answer: with the handler following it the address would be
    /// spent fetching the result page and the header carrying it would be gone. Cookies are off because the
    /// wrapper was measured on 2026-09-14 to answer a client that has none, and a session sent where it is not
    /// needed is one more thing to be recognised by.
    /// <para>
    /// Deliberately not <c>AddSearchClient</c>. That one holds the per-host gate, and this engine's gate is the
    /// two minutes set just above — a seven-link answer would take a quarter of an hour to resolve through it,
    /// because the gate paces searches and these are the links of one search. The gap between two follows is
    /// <see cref="GoogleOptions.FollowDelay"/> instead, and unlike the ceiling it is a rate measured to work.
    /// </para>
    /// <para>
    /// It leaves by the configured exit all the same, which it did not until 2026-09-14: a bare handler was
    /// built here and the engine's own address saw the search arrive from the proxy and the link resolved from
    /// somewhere else moments later. The link it is resolving is an encrypted blob that engine issued to that
    /// session, so it is the one party able to join the two addresses, and it is handed the join.
    /// </para>
    /// </remarks>
    private static void Following(this IServiceCollection services)
        => services
            .AddHttpClient(
                Google.ProviderName,
                (provider, client) => client.Timeout = provider.GetRequiredService<IOptions<SearchOptions>>().Value.Timeout)
            .ConfigurePrimaryHttpMessageHandler(provider => Proxied.Handler(provider, exit =>
            {
                exit.AllowAutoRedirect = false;
                exit.UseCookies = false;
            }));

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
