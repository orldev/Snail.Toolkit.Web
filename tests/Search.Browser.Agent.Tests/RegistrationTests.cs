using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping.Agent;
using Snail.Toolkit.Web.Search.Browser.Agent.Extensions;
using Snail.Toolkit.Web.Search.Providers;

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

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(Google.ProviderName);

        Assert.False(Assert.IsType<HttpClientHandler>(Primary(handler)).AllowAutoRedirect);
    }

    /// <summary>Cookies are off: the wrapper was measured to answer a client that has none.</summary>
    [Fact]
    public async Task AddGoogle_ClientThatAsksAboutAWrapper_CarriesNoSession()
    {
        await using var provider = Built([]);

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(Google.ProviderName);

        Assert.False(Assert.IsType<HttpClientHandler>(Primary(handler)).UseCookies);
    }

    private static HttpMessageHandler Primary(HttpMessageHandler handler)
    {
        while (handler is DelegatingHandler forwarding && forwarding.InnerHandler is { } inner)
            handler = inner;

        return handler;
    }

    private static ServiceProvider Built(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddGoogle(configuration);

        return services.BuildServiceProvider();
    }
}
