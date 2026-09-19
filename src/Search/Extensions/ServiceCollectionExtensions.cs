using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Extensions;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Search.Http;
using Snail.Toolkit.Web.Search.Providers;

namespace Snail.Toolkit.Web.Search.Extensions;

/// <summary>
/// Registers the providers, their HTTP clients, the memory of refusals and the search that merges them.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers search, binding every option class from configuration.</summary>
        public IServiceCollection AddSearch(IConfiguration configuration, Action<SearchOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddWeb(configuration);

            var search = services.BindOnce<SearchOptions>(configuration, SearchOptions.SectionName);

            if (configure is not null)
                search.Configure(configure);

            Validated(search);

            return services.AddSearchParts();
        }

        /// <summary>Registers search configured in code.</summary>
        public IServiceCollection AddSearch(Action<SearchOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            services.AddWeb();

            Validated(services.AddOptions<SearchOptions>().Configure(configure));

            return services.AddSearchParts();
        }

        /// <summary>Adds one more provider to the set every query is run against.</summary>
        /// <remarks>
        /// Registered as an enumerable so providers accumulate instead of replacing each other, and idempotent
        /// per implementation type so a second call cannot ask the same back end twice per query.
        /// </remarks>
        public IServiceCollection AddSearchProvider<TProvider>()
            where TProvider : class, ISearchProvider
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ISearchProvider, TProvider>());

            return services;
        }

        /// <summary>Adds the named HTTP client a provider fetches through.</summary>
        /// <remarks>
        /// The order of the handlers is the point. Resilience is outermost, so every retry re-enters the
        /// politeness gate and is made under a freshly drawn identity; the other way round, a retry storm would
        /// hold one host's gate for the whole backoff and repeat the same user agent that was just refused.
        /// <para>
        /// The deadline is the resilience pipeline's, not the client's: the standard handler sets
        /// <see cref="HttpClient.Timeout"/> to infinite so that its own total-request timeout owns the clock.
        /// Setting the client timeout beside it is therefore a setting that silently does nothing, which is why
        /// <see cref="SearchOptions.Timeout"/> is applied here instead — together with the per-attempt timeout
        /// and the sampling window, because the pipeline refuses a total shorter than the attempt it contains.
        /// </para>
        /// The exit is the configured one, which it had not been until 2026-09-14. The browser has always
        /// honoured <c>Web:Identity:Proxies</c> because its context is built from the identity, while this
        /// client set no primary handler at all and went out directly. The consequence worth naming is not
        /// the leak but the correlation: a query arrives from the proxy and the request that resolves its
        /// links arrives from the real address seconds later, and the party seeing both is the one that
        /// handed out the link.
        /// <para>
        /// The builder is handed back so a consumer can add a header of their own, or replace the exit where
        /// one client has to leave by another route.
        /// </para>
        /// </remarks>
        public IHttpClientBuilder AddSearchClient(string name)
        {
            services.TryAddTransient<PolitenessHandler>();
            services.TryAddTransient<IdentityHandler>();

            var client = services.AddHttpClient(name);

            client.ConfigurePrimaryHttpMessageHandler(provider => Proxied.Handler(provider));

            client.AddStandardResilienceHandler().Configure((resilience, provider) =>
            {
                var budget = provider.GetRequiredService<IOptions<SearchOptions>>().Value.Timeout;
                var longestRest = provider.GetRequiredService<IOptions<PolitenessOptions>>().Value.LongestRest;
                var wasTransient = resilience.Retry.ShouldHandle;

                resilience.TotalRequestTimeout.Timeout = budget;
                resilience.AttemptTimeout.Timeout = budget / 3;
                resilience.CircuitBreaker.SamplingDuration = budget / 3 * 2;

                resilience.Retry.ShouldHandle = async arguments =>
                    await wasTransient(arguments) && IsWorthRepeating(arguments.Outcome.Result, longestRest);
            });

            client.AddHttpMessageHandler<PolitenessHandler>();
            client.AddHttpMessageHandler<IdentityHandler>();

            return client;
        }

        /// <summary>Answers whether asking again could do better than repeating the refusal.</summary>
        /// <remarks>
        /// The standard handler retries a <c>429</c> and waits the <c>Retry-After</c> the site sent, which is right
        /// until the number is larger than the caller has. Measured on 2026-09-14 against a handler that counts what
        /// reaches it: a gap of three seconds cost four requests and nine seconds, inside a thirty-second budget; a
        /// gap of thirty, inside a budget of twenty, made one request and then spent the whole budget waiting to be
        /// refused a second time, ending in a timeout that named neither the status nor the gap. The site had
        /// already said both in the first answer, and this hands that answer back instead of sitting on it.
        /// <para>
        /// The gate still holds the host for the full gap — <see cref="Http.PolitenessHandler"/> writes it there on
        /// the way out — so declining to wait here makes nobody ruder, only better informed.
        /// </para>
        /// <para>
        /// A refusal that names no gap is not repeated either, which it was until 2026-09-20: a <c>429</c> or a
        /// <c>503</c> without the header cost four requests before the search heard about the first one, and the
        /// search rests an engine the moment it hears. Asking a challenged engine again at once is how a pause
        /// becomes a ban — the rule this toolkit states everywhere and practised against itself here. A server
        /// that merely failed is repeated as before: a <c>500</c> is not the site turning anybody away.
        /// </para>
        /// </remarks>
        private static bool IsWorthRepeating(HttpResponseMessage? answered, TimeSpan longestRest)
        {
            if (answered?.Headers.RetryAfter?.Delta is { } gap)
                return gap <= longestRest;

            return answered is null || !Refusals.IsRefusal(answered.StatusCode);
        }

        /// <summary>Registers the providers that answer a plain request, the search that merges them, and the memory it consults first.</summary>
        /// <remarks>
        /// Two engines rather than one, because which of them answers a given client from a given address is a
        /// fact that changes: measured on 2026-09-12 from a datacentre address, one returned results while
        /// several others answered with a challenge. Each provider is one request per query, so a deployment
        /// that wants only one registers it on its own instead of taking the pair.
        /// <para>
        /// The memory of refusals is one per process on purpose — a rest recorded by one search has to hold
        /// for the next — and the clock it reads is registered beside it, so a host that keeps its own
        /// <see cref="TimeProvider"/> wins and a test can move time instead of waiting it out.
        /// </para>
        /// </remarks>
        private IServiceCollection AddSearchParts()
        {
            services.AddSearchProvider<DuckDuckGo>();
            services.AddSearchClient(DuckDuckGo.ProviderName);

            services.AddSearchProvider<Bing>();
            services.AddSearchClient(Bing.ProviderName);

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ISearchCooldown, SearchCooldown>();
            services.TryAddSingleton<IWebSearch, WebSearch>();

            return services;
        }
    }

    private static void Validated(OptionsBuilder<SearchOptions> builder)
        => builder
            .Validate(
                settings => settings.MaxDegreeOfParallelism > 0,
                $"'{SearchOptions.SectionName}:{nameof(SearchOptions.MaxDegreeOfParallelism)}' has to be positive.")
            .Validate(
                settings => settings.MaxResultsPerProvider > 0,
                $"'{SearchOptions.SectionName}:{nameof(SearchOptions.MaxResultsPerProvider)}' has to be positive.")
            .Validate(
                settings => settings.Timeout >= TimeSpan.FromSeconds(1),
                $"'{SearchOptions.SectionName}:{nameof(SearchOptions.Timeout)}' has to be at least one second.")
            .Validate(
                settings => settings.RefusalCooldown > TimeSpan.Zero,
                $"'{SearchOptions.SectionName}:{nameof(SearchOptions.RefusalCooldown)}' has to be positive.")
            .Validate(
                settings => settings.MaxRefusalCooldown >= settings.RefusalCooldown,
                $"'{SearchOptions.SectionName}:{nameof(SearchOptions.MaxRefusalCooldown)}' cannot be shorter than "
                + $"'{nameof(SearchOptions.RefusalCooldown)}'.")
            .Validate(
                settings => settings.ProvidersPerQuery > 0,
                $"'{SearchOptions.SectionName}:{nameof(SearchOptions.ProvidersPerQuery)}' has to be positive.")
            .Validate(
                settings => settings.AlwaysAsked.All(name => !string.IsNullOrWhiteSpace(name)),
                $"'{SearchOptions.SectionName}:{nameof(SearchOptions.AlwaysAsked)}' holds a blank provider name.")
            .ValidateOnStart();
}
