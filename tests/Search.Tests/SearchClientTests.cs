using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Snail.Toolkit.Web.Extensions;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Search.Extensions;

namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// What the client a provider fetches through does with a site that refuses by frequency.
/// </summary>
/// <remarks>
/// Driven through the real pipeline rather than a stubbed factory, because the behaviour under test belongs to
/// the pipeline: the standard resilience handler decides whether to ask again, and the politeness handler is
/// the only thing on this road that sees the response headers. A fake <c>IHttpClientFactory</c>, which is how
/// the providers are tested, would have neither.
/// </remarks>
public class SearchClientTests
{
    private const string Address = "http://site.invalid/busy";

    /// <summary>
    /// A gap longer than we will wait is believed the first time and handed straight back.
    /// </summary>
    /// <remarks>
    /// Measured on 2026-09-14 against this same counting handler: before the ceiling, a gap of thirty seconds
    /// inside a budget of twenty made one request and then spent the entire budget waiting to be refused a
    /// second time, ending in a <c>TimeoutRejectedException</c> that named neither the status nor the gap.
    /// The site had said both in its first answer.
    /// </remarks>
    [Fact]
    public async Task SendAsync_GapLongerThanWeWillWait_AsksOnceAndReturnsTheRefusal()
    {
        var counting = new Counting(TimeSpan.FromSeconds(30));

        await using var provider = Host(counting, budget: TimeSpan.FromSeconds(20));

        var answered = await Client(provider).GetAsync(Address);

        Assert.Equal(1, counting.Sent);
        Assert.Equal(HttpStatusCode.TooManyRequests, answered.StatusCode);
    }

    /// <summary>A gap short enough to sit out is still sat out, and the request still repeated.</summary>
    /// <remarks>
    /// Without this one, a ceiling of zero would pass the check above and switch off retrying altogether.
    /// </remarks>
    [Fact]
    public async Task SendAsync_GapShorterThanWeWillWait_IsStillRepeated()
    {
        var counting = new Counting(TimeSpan.FromSeconds(1));

        await using var provider = Host(counting, budget: TimeSpan.FromSeconds(20));

        await Client(provider).GetAsync(Address);

        Assert.Equal(4, counting.Sent);
    }

    /// <summary>A gap named as a date short enough to sit out is repeated, as the same gap in seconds is.</summary>
    /// <remarks>
    /// Found by the audit of 2026-10-23: the decision read the seconds and nothing else, so this answer was
    /// handed back after one request while the gate, reading the same header, held the host for the date.
    /// </remarks>
    [Fact]
    public async Task SendAsync_GapNamedAsADateShorterThanWeWillWait_IsStillRepeated()
    {
        var counting = new Counting(TimeSpan.FromSeconds(1), isDated: true);

        await using var provider = Host(counting, budget: TimeSpan.FromSeconds(20));

        await Client(provider).GetAsync(Address);

        Assert.Equal(4, counting.Sent);
    }

    /// <summary>A gap named as a date longer than we will wait is believed the first time, as in seconds.</summary>
    [Fact]
    public async Task SendAsync_GapNamedAsADateLongerThanWeWillWait_AsksOnce()
    {
        var counting = new Counting(TimeSpan.FromSeconds(30), isDated: true);

        await using var provider = Host(counting, budget: TimeSpan.FromSeconds(20));

        await Client(provider).GetAsync(Address);

        Assert.Equal(1, counting.Sent);
    }

    /// <summary>
    /// The gap the site named reaches the gate, so every later caller to that host waits with us.
    /// </summary>
    /// <remarks>
    /// Until 2026-09-14 <c>Rest</c> had exactly one caller, on the browser side, so a <c>429</c> answered to a
    /// search engine slowed nothing at all — and a browser visit to the same host walked straight into a site
    /// that had just said it had had enough.
    /// </remarks>
    [Fact]
    public async Task SendAsync_SiteThatAskedToBeLeftAlone_PassesTheGapToTheGate()
    {
        var counting = new Counting(TimeSpan.FromSeconds(30));
        var gate = new CountingPoliteness();

        await using var provider = Host(counting, budget: TimeSpan.FromSeconds(20), gate);

        await Client(provider).GetAsync(Address);

        Assert.Equal(TimeSpan.FromSeconds(30), gate.Rested);
    }

    /// <summary>A site that asked for nothing is held to nothing beyond the configured pace.</summary>
    [Fact]
    public async Task SendAsync_ResponseWithoutAGap_LeavesTheGateAlone()
    {
        var gate = new CountingPoliteness();

        await using var provider = Host(new Counting(asked: null), budget: TimeSpan.FromSeconds(20), gate);

        await Client(provider).GetAsync(Address);

        Assert.Null(gate.Rested);
    }

    /// <summary>
    /// A refusal that names no gap is not repeated either: the engine said no, and asking three more times is
    /// how a pause becomes a ban — the same rule the browser path follows since 2026-09-20.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SendAsync_RefusalWithoutAGap_AsksOnce(HttpStatusCode status)
    {
        var counting = new Counting(null, status);

        await using var provider = Host(counting, budget: TimeSpan.FromSeconds(20));

        await Client(provider).GetAsync(Address);

        Assert.Equal(1, counting.Sent);
    }

    /// <summary>
    /// A server that failed is not a server that refused. Without this one, declining to repeat every refusal
    /// would pass the check above by switching off retries for everything the pipeline handles.
    /// </summary>
    [Fact]
    public async Task SendAsync_ServerThatFailed_IsStillRepeated()
    {
        var counting = new Counting(null, HttpStatusCode.InternalServerError);

        await using var provider = Host(counting, budget: TimeSpan.FromSeconds(20));

        await Client(provider).GetAsync(Address);

        Assert.Equal(4, counting.Sent);
    }

    private static HttpClient Client(ServiceProvider provider)
        => provider.GetRequiredService<IHttpClientFactory>().CreateClient("probe");

    private static ServiceProvider Host(Counting counting, TimeSpan budget, IPoliteness? politeness = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddWeb();
        services.AddOptions<SearchOptions>().Configure(options => options.Timeout = budget);
        services.AddOptions<PolitenessOptions>().Configure(options => options.LongestRest = TimeSpan.FromSeconds(10));
        services.AddSearchClient("probe").ConfigurePrimaryHttpMessageHandler(() => counting);

        if (politeness is not null)
            services.AddSingleton(politeness);

        return services.BuildServiceProvider();
    }

    /// <summary>A site that always refuses by frequency, counting what reaches it.</summary>
    private sealed class Counting(
        TimeSpan? asked,
        HttpStatusCode status = HttpStatusCode.TooManyRequests,
        bool isDated = false)
        : HttpMessageHandler
    {
        public int Sent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Sent++;

            var answered = new HttpResponseMessage(status);

            if (asked is { } gap)
                answered.Headers.RetryAfter = isDated
                    ? new RetryConditionHeaderValue(DateTimeOffset.UtcNow + gap)
                    : new RetryConditionHeaderValue(gap);

            return Task.FromResult(answered);
        }
    }

    private sealed class CountingPoliteness : IPoliteness
    {
        public DateTimeOffset OpensAt(Uri url) => DateTimeOffset.MinValue;

        public TimeSpan? Rested { get; private set; }

        public Task<IAsyncDisposable> EnterAsync(Uri url, TimeSpan? requested = null, CancellationToken ct = default)
            => Task.FromResult<IAsyncDisposable>(new Turn());

        public void Rest(Uri url, TimeSpan gap) => Rested = gap;

        private sealed class Turn : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
