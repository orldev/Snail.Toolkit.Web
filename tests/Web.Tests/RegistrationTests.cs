using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Extensions;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Robots;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the composition of the core: everything it offers has to resolve from one call.
/// </summary>
public class RegistrationTests
{
    [Theory]
    [InlineData(typeof(IIdentityRotation))]
    [InlineData(typeof(IProxyRotation))]
    [InlineData(typeof(IPoliteness))]
    [InlineData(typeof(IBlockDetector))]
    [InlineData(typeof(ITextExtraction))]
    [InlineData(typeof(ILinkExtraction))]
    public void AddWeb_EveryContractTheCoreOffers_Resolves(Type contract)
    {
        using var services = Built();

        Assert.NotNull(services.GetRequiredService(contract));
    }

    /// <summary>
    /// Robots rules stay out of the core registration because obeying them means downloading a file, and the
    /// core owns no transport: asking for them before an engine is added must fail loudly rather than at the
    /// first URL.
    /// </summary>
    [Fact]
    public void AddWeb_WithoutATransport_OffersNoRobotsRules()
    {
        using var services = Built();

        Assert.Null(services.GetService<ISiteRules>());
    }

    [Fact]
    public void AddSiteRules_WithATransport_ResolvesTheRulesAndTheSource()
    {
        using var services = Built(collection => collection.AddSiteRules<OpenSource>());

        Assert.NotNull(services.GetRequiredService<ISiteRules>());
        Assert.IsType<OpenSource>(services.GetRequiredService<IRobotsSource>());
    }

    [Fact]
    public void AddWeb_Configuration_ReachesTheOptions()
    {
        using var services = Built();

        var politeness = services.GetRequiredService<IOptions<PolitenessOptions>>().Value;

        Assert.Equal(4, politeness.MaxConcurrentPerHost);
        Assert.Equal(TimeSpan.FromSeconds(5), politeness.HostDelays["search.example"]);
        Assert.True(services.GetRequiredService<IOptions<RobotsOptions>>().Value.IsObeyed);
        Assert.Equal(["http://one.example:8080"], services.GetRequiredService<IOptions<IdentityOptions>>().Value.Proxies);
    }

    /// <summary>A gap below zero is not a setting but a typo, and it is refused where the message can name it.</summary>
    [Theory]
    [InlineData("Web:Politeness:MinDelayPerHost", "-00:00:01")]
    [InlineData("Web:Politeness:MaxConcurrentPerHost", "0")]
    [InlineData("Web:Politeness:HostDelays:slow.example", "-00:00:01")]
    public void AddWeb_PolitenessSettingThatCannotWork_FailsValidation(string key, string value)
    {
        using var services = Built(settings: new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<PolitenessOptions>>().Value);
    }

    private static ServiceProvider Built(
        Action<IServiceCollection>? extra = null,
        Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Web:Politeness:MaxConcurrentPerHost"] = "4",
                ["Web:Politeness:HostDelays:search.example"] = "00:00:05",
                ["Web:Robots:IsObeyed"] = "true",
                ["Web:Identity:Proxies:0"] = "http://one.example:8080"
            })
            .AddInMemoryCollection(settings ?? [])
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWeb(configuration);
        extra?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private sealed class OpenSource : IRobotsSource
    {
        public Task<RobotsFile> ReadAsync(Uri origin, ProxyEndpoint? proxy, CancellationToken ct = default)
            => Task.FromResult(RobotsFile.AllowAll);
    }
}
