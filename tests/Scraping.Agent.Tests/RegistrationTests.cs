using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping.Agent.Extensions;

namespace Snail.Toolkit.Web.Scraping.Agent.Tests;

public class RegistrationTests
{
    [Fact]
    public void AddPageAgent_ValidConfiguration_BindsTheSettleWindow()
    {
        var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Scraping:Agent:Settle"] = "00:00:07"
        });

        var settings = provider.GetRequiredService<IOptions<AgentOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(7), settings.Settle);
    }

    /// <summary>The agent pulls the whole scraping stack in with it, so one call is enough to resolve it.</summary>
    [Fact]
    public void AddPageAgent_NothingConfigured_ResolvesTheAgent()
    {
        var provider = Built(new Dictionary<string, string?>());

        Assert.NotNull(provider.GetRequiredService<IPageAgent>());
    }

    [Fact]
    public void AddPageAgent_NegativeSettleWindow_IsRefused()
    {
        var provider = Built(new Dictionary<string, string?>
        {
            ["Web:Scraping:Agent:Settle"] = "-00:00:01"
        });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AgentOptions>>().Value);
    }

    private static ServiceProvider Built(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddPageAgent(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        return services.BuildServiceProvider();
    }
}
