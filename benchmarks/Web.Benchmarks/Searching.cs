using System.Net;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snail.Toolkit.Web.Search;
using Snail.Toolkit.Web.Search.Extensions;
using Snail.Toolkit.Web.Search.Providers;

namespace Snail.Toolkit.Web.Benchmarks;

/// <summary>What a DuckDuckGo query costs on the library's own HTTP pipeline, its answer served from memory.</summary>
/// <remarks>
/// The client is the one the library registers — resilience, the per-host gate, the identity headers — with only
/// the transport replaced, and the gate's delay set to nothing so that the timing is of the work rather than of
/// the wait.
/// </remarks>
[MemoryDiagnoser]
public class Searching
{
    private ServiceProvider _services = null!;

    private ISearchProvider _engine = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Web:Politeness:MinDelayPerHost"] = "00:00:00" })
            .Build();
        var services = new ServiceCollection();
        var html = DuckDuckGoResults.Build();

        services.AddLogging();
        services.AddSearch(configuration);
        services.AddHttpClient(DuckDuckGo.ProviderName).ConfigurePrimaryHttpMessageHandler(() => new Canned(html));

        _services = services.BuildServiceProvider();
        _engine = _services.GetServices<ISearchProvider>().OfType<DuckDuckGo>().Single();

        var answer = await _engine.FindAsync(DuckDuckGoResults.Query);

        if (!answer.IsSuccess || answer.Value.Count != 30)
            throw new InvalidOperationException("DuckDuckGo did not answer the thirty results its page carries.");
    }

    [GlobalCleanup]
    public void Cleanup() => _services.Dispose();

    [Benchmark]
    public async Task<int> DuckDuckGoPage()
    {
        var answer = await _engine.FindAsync(DuckDuckGoResults.Query);

        return answer.Value.Count;
    }

    private sealed class Canned(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
    }
}
