using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.Toolkit.Web.Politeness;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping.Agent;
using Snail.Toolkit.Web.Search.Browser.Agent.Extensions;
using Snail.Toolkit.Web.Search.Providers;
using Snail.Toolkit.Web.Tests;

namespace Snail.Toolkit.Web.Search.Browser.Agent.Tests;

/// <summary>
/// Covers the composition: this package is search plus the page agent, and both halves have to be there.
/// </summary>
public class RegistrationTests
{
    [Fact]
    public async Task AddGoogle_ProviderSet_HoldsTheHttpProvidersAndGoogle()
    {
        await using var provider = Built([]);

        var names = provider.GetRequiredService<IEnumerable<ISearchProvider>>().Select(p => p.Name).Order();

        Assert.Equal([Bing.ProviderName, DuckDuckGo.ProviderName, Google.ProviderName], names);
    }

    /// <summary>Google is walked through its form, so the agent has to be registered alongside it.</summary>
    [Fact]
    public async Task AddGoogle_PageAgent_IsRegisteredForTheProviderToWalkWith()
    {
        await using var provider = Built([]);

        Assert.NotNull(provider.GetRequiredService<IPageAgent>());
    }

    [Fact]
    public async Task AddGoogle_Domain_ComesFromConfiguration()
    {
        await using var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Search:Google:Domain"] = "www.google.de"
        });

        Assert.Equal("www.google.de", provider.GetRequiredService<IOptions<GoogleOptions>>().Value.Domain);
    }

    /// <summary>
    /// A domain written as a URL is the mistake this setting invites, and it produces an address like
    /// <c>https://https://www.google.de/</c> that fails far from its cause.
    /// </summary>
    [Theory]
    [InlineData("https://www.google.de")]
    [InlineData("www.google.de/search")]
    [InlineData("")]
    public async Task AddGoogle_DomainThatIsNotAHost_FailsValidation(string domain)
    {
        await using var provider = Built(new Dictionary<string, string?> { ["Web:Search:Google:Domain"] = domain });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<GoogleOptions>>().Value);
    }

    /// <summary>Registration is idempotent, so a second call cannot ask the engine twice per query.</summary>
    [Fact]
    public async Task AddGoogle_CalledTwice_AsksTheProviderOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddGoogle(new ConfigurationBuilder().Build());
        services.AddGoogle(new ConfigurationBuilder().Build());

        await using var provider = services.BuildServiceProvider();

        Assert.Equal(3, provider.GetRequiredService<IEnumerable<ISearchProvider>>().Count());
    }

    /// <summary>Registration is idempotent in its settings too, not only in its services.</summary>
    /// <remarks>
    /// One call reaches the core's registration through both search and the agent, binding its sections twice.
    /// Binding appends to a list rather than replacing it: measured on 2026-09-13, two configured proxies came
    /// back as four and a Chromium switch was passed twice.
    /// </remarks>
    [Fact]
    public async Task AddGoogle_ReachingTheCoreThroughBothHalves_BindsTheIdentityListOnce()
    {
        await using var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Identity:Proxies:0"] = "http://one.example:8080",
            ["Web:Identity:Proxies:1"] = "http://two.example:8080"
        });

        var identity = provider.GetRequiredService<IOptions<Identity.IdentityOptions>>().Value;

        Assert.Equal(["http://one.example:8080", "http://two.example:8080"], identity.Proxies);
    }

    /// <summary>The redirect is the answer, so the client that asks about a wrapper must not follow it.</summary>
    /// <remarks>
    /// A handler that follows it spends the address fetching the result page, and the <c>Location</c> header
    /// carrying it is gone by the time anyone reads the reply — every result would come back as the page it
    /// points at, fetched twice and read by nobody. The default for a new handler is to follow.
    /// </remarks>
    [Fact]
    public async Task AddGoogle_ClientThatAsksAboutAWrapper_DoesNotFollowTheRedirect()
    {
        await using var provider = Built([]);

        Assert.False(Exit(provider).AllowAutoRedirect);
    }

    /// <summary>Cookies are off: the wrapper was measured to answer a client that has none.</summary>
    [Fact]
    public async Task AddGoogle_ClientThatAsksAboutAWrapper_CarriesNoSession()
    {
        await using var provider = Built([]);

        Assert.False(Exit(provider).UseCookies);
    }

    /// <summary>Asking where a wrapped link leads goes by the configured exit, like the search that produced it.</summary>
    /// <remarks>
    /// This is the road where a direct exit costs most. The link is an encrypted blob the engine issued to
    /// that session, so the engine is the one party able to join the address the query came from to the
    /// address the link was opened from, and until 2026-09-14 a bare handler here handed it that join
    /// seconds after the search.
    /// </remarks>
    [Fact]
    public async Task AddGoogle_WithAnExitConfigured_AsksAboutTheWrapperThroughIt()
    {
        await using var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Identity:Proxies:0"] = "http://exit.example:8080"
        });

        Assert.Equal(
            new Uri("http://exit.example:8080/"),
            Outgoing.ExitOf(provider, Google.ProviderName, new Uri("https://www.google.com/url?q=1")));
    }

    /// <summary>Nothing is invented when no exit is configured, which is what makes the check above mean something.</summary>
    [Fact]
    public async Task AddGoogle_WithNoExitConfigured_GoesOutDirectly()
    {
        await using var provider = Built([]);

        Assert.Null(Outgoing.ExitOf(provider, Google.ProviderName, new Uri("https://www.google.com/url?q=1")));
    }

    /// <summary>The engine is given the interval it was measured to answer at, unless one is named for it.</summary>
    [Fact]
    public async Task AddGoogle_WithNoGapNamed_PacesTheEngineAtTheMeasuredInterval()
    {
        await using var provider = Built([]);

        Assert.Equal(TimeSpan.FromMinutes(2), Politeness(provider).GapFor("www.google.com"));
    }

    /// <summary>
    /// A key above the host is how <c>HostDelays</c> is documented to cover it, so a deployment writing
    /// <c>google.com</c> has named this engine's gap and the package must not write one beside it.
    /// </summary>
    /// <remarks>
    /// The louder half of the defect this covers: until 2026-09-15 the check was an exact-key lookup, so a
    /// deployment asking for FIVE MINUTES had <c>www.google.com = 2 minutes</c> written next to it, and the
    /// more specific key wins — the package quietly asked the engine more often than the deployment allowed.
    /// </remarks>
    [Fact]
    public async Task AddGoogle_WithTheHostAboveItNamed_LeavesThatGapStanding()
    {
        await using var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Politeness:HostDelays:google.com"] = "00:05:00"
        });

        var politeness = Politeness(provider);

        Assert.Equal(TimeSpan.FromMinutes(5), politeness.GapFor("www.google.com"));
        Assert.DoesNotContain("www.google.com", politeness.HostDelays.Keys, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A deployment may buy the refusals, and is told what it bought.</summary>
    /// <remarks>
    /// The gap most likely to be found in a configuration file is the one written for the engine this package
    /// replaced, which was asked by address and paced in seconds. It stands — the deployment decides — but a
    /// setting that quietly turns a measured floor into a rate the engine answers with a challenge is worth a
    /// line naming both numbers.
    /// </remarks>
    [Fact]
    public async Task AddGoogle_AskedFasterThanMeasured_KeepsTheNamedGapAndSaysSo()
    {
        var recorded = new Recording();

        await using var provider = Built(
            new Dictionary<string, string?> { ["Web:Politeness:HostDelays:google.com"] = "00:00:05" },
            recorded);

        Assert.Equal(TimeSpan.FromSeconds(5), Politeness(provider).GapFor("www.google.com"));
        Assert.Contains(recorded.Warnings, line => line.Contains("00:00:05") && line.Contains("00:02:00"));
    }

    /// <summary>The line is said only where it is news: a gap at or above the measurement is not.</summary>
    [Fact]
    public async Task AddGoogle_AskedSlowerThanMeasured_SaysNothing()
    {
        var recorded = new Recording();

        await using var provider = Built(
            new Dictionary<string, string?> { ["Web:Politeness:HostDelays:google.com"] = "00:05:00" },
            recorded);

        Politeness(provider);

        Assert.Empty(recorded.Warnings);
    }

    private static PolitenessOptions Politeness(IServiceProvider provider)
        => provider.GetRequiredService<IOptions<PolitenessOptions>>().Value;

    private static SocketsHttpHandler Exit(IServiceProvider provider)
        => Assert.IsType<SocketsHttpHandler>(Outgoing.HandlerOf(provider, Google.ProviderName));

    private static ServiceProvider Built(Dictionary<string, string?> settings, Recording? recording = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            if (recording is not null)
                logging.AddProvider(recording);
        });

        services.AddGoogle(configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>Keeps the warnings written during registration, so a line meant for a person can be asserted on.</summary>
    private sealed class Recording : ILoggerProvider, ILogger
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings => _warnings;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (logLevel >= LogLevel.Warning)
                _warnings.Add(formatter(state, exception));
        }

        public void Dispose()
        {
        }
    }
}
