using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Snail.Toolkit.Web.Politeness;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping.Agent;

namespace Snail.Toolkit.Web.Search.Browser.Agent.Tests;

/// <summary>
/// Covers the two things Google contributes over <see cref="BrowserSearch"/>: its errand and its markup.
/// </summary>
public class GoogleTests
{
    private const string Page =
        """
        <html><body><div id="search">
          <div data-hveid="a1">
            <a href="https://example.com/grain"><h3>Grain &amp; barley report</h3></a>
            <div data-sncf="1">Wheat prices rose &amp; barley held steady.</div>
          </div>
          <div data-hveid="a2">
            <a href="/url?q=https%3A%2F%2Fwrapped.example%2Fpage&amp;sa=U"><h3>Wrapped result</h3></a>
          </div>
          <div data-hveid="a3">
            <a href="https://accounts.google.com/ServiceLogin"><h3>Sign in</h3></a>
          </div>
          <div data-hveid="a4">
            <a href="https://www.google.com/search?q=more"><h3>More results</h3></a>
          </div>
          <div data-hveid="a5">
            <a href="https://developers.google.com/search/docs"><h3>Google Search Central</h3></a>
          </div>
          <div data-hveid="a6">
            <a href="https://example.com/no-heading">no heading here</a>
          </div>
          <div data-hveid="a7">
            <a href="/goto?url=CAESWAHrOzAVblob"><h3>Wrapped in a blob</h3></a>
          </div>
        </div></body></html>
        """;

    private const string TwoWrappers =
        """
        <html><body><div id="search">
          <div data-hveid="b1"><a href="/goto?url=CAESWAHrOzAVone"><h3>First</h3></a></div>
          <div data-hveid="b2"><a href="/goto?url=CAESWAHrOzAVtwo"><h3>Second</h3></a></div>
        </div></body></html>
        """;

    /// <summary>Anchored on a link containing a heading, which is the one shape that survives layout churn.</summary>
    [Fact]
    public async Task FindAsync_OrdinaryResult_ReadsTitleAddressAndSnippet()
    {
        var first = (await Found(Page))[0];

        Assert.Equal("Grain & barley report", first.Title);
        Assert.Equal("https://example.com/grain", first.Address.Canonical);
        Assert.Equal("Wheat prices rose & barley held steady.", first.Snippet);
    }

    [Fact]
    public async Task FindAsync_WrappedLink_UnwrapsTheRealAddress()
        => Assert.Equal("https://wrapped.example/page", (await Found(Page))[1].Address.Canonical);

    /// <summary>Sign-in, policies, help and the search host itself are the page's furniture, not answers.</summary>
    [Theory]
    [InlineData("accounts.google.com")]
    [InlineData("www.google.com")]
    public async Task FindAsync_GooglesOwnFurniture_IsNotAResult(string host)
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Address?.Host == host);

    /// <summary>
    /// A result that genuinely lives on a Google domain is still a result: excluding everything Google-shaped
    /// would drop real answers to real queries.
    /// </summary>
    [Fact]
    public async Task FindAsync_GenuineResultOnAGoogleDomain_IsKept()
        => Assert.Contains(await Found(Page), result => result.Address.Canonical == "https://developers.google.com/search/docs");

    [Fact]
    public async Task FindAsync_LinkWithoutAHeading_IsSkipped()
        => Assert.DoesNotContain(await Found(Page), result => result.Address.Canonical.Contains("no-heading", StringComparison.Ordinal));

    /// <summary>Image, video and knowledge blocks carry a link and a heading with no prose at all.</summary>
    [Fact]
    public async Task FindAsync_ResultWithoutASnippet_ReportsAnEmptyOne()
        => Assert.Equal(string.Empty, (await Found(Page))[1].Snippet);

    [Fact]
    public async Task FindAsync_EveryResult_NamesTheProvider()
        => Assert.All(await Found(Page), result => Assert.Equal(Google.ProviderName, result.Provider));

    [Fact]
    public async Task FindAsync_PageThatLoaded_ReturnsOnlyTheOutboundLinks()
        => Assert.Equal(4, (await Found(Page)).Count);

    [Fact]
    public async Task FindAsync_MarkupItCannotFollow_ReportsNothing()
        => Assert.Empty(await Found("<html><body><div>we redesigned everything</div></body></html>"));

    /// <summary>The errand starts on the home page of the configured front end, not on a results address.</summary>
    /// <remarks>
    /// Measured on 2026-09-13 from one address, a fresh context per arm and twelve minutes between them: the
    /// results address answered <c>/sorry/</c> where the same query typed into the form returned seven
    /// results, with a passing arm on either side of the refusal.
    /// </remarks>
    [Fact]
    public async Task FindAsync_Query_StartsOnTheConfiguredHomePage()
    {
        var agent = new StubAgent(_ => Fixture.Walked(Page));

        await Provider(agent, "www.google.de").FindAsync("grain & barley");

        Assert.Equal("https://www.google.de/", agent.Performed?.Address.Address?.AbsoluteUri);
    }

    /// <summary>The query is typed into the field rather than escaped into an address.</summary>
    /// <remarks>
    /// The form adds <c>source=hp</c>, <c>gs_lp</c> with the typing log, <c>ved</c> and <c>sclient=gws-wiz</c>
    /// to the address it submits — parameters a hand-built address does not carry and nothing can forge.
    /// </remarks>
    [Fact]
    public async Task FindAsync_Query_IsTypedIntoTheFieldVerbatim()
    {
        var agent = new StubAgent(_ => Fixture.Walked(Page));

        await Provider(agent, "www.google.de").FindAsync("grain & barley");

        var typed = agent.Performed?.Steps[0];

        Assert.Equal(StepKind.Type, typed?.Kind);
        Assert.Equal("grain & barley", typed?.Value);
    }

    /// <summary>The return key is what submits this form; two of five engines offer nothing else.</summary>
    [Fact]
    public async Task FindAsync_AfterTyping_PressesTheReturnKey()
        => Assert.Equal(StepKind.Press, (await Performed()).Steps[1].Kind);

    /// <summary>
    /// Every result Google shows is a wrapper whose query is an encrypted blob, so the address it means is
    /// known only by asking the engine where the link leads.
    /// </summary>
    /// <remarks>
    /// Measured on 2026-09-14: the wrapper answers <c>302</c> carrying the address in <c>Location</c>, to a
    /// client with no cookies, no referer and no session. Until then the relative wrapper reached the outbound
    /// filter, which reads a link with no host as the engine's own and dropped every result on the page.
    /// </remarks>
    [Fact]
    public async Task FindAsync_WrappedInABlob_AnswersWithTheAddressBehindIt()
        => Assert.Equal("https://unwrapped.example/1", (await Found(Page)).Last().Address.Canonical);

    [Fact]
    public async Task FindAsync_Wrapper_IsAskedAtTheConfiguredFrontEnd()
    {
        var clients = new StubClients();

        await Provider(new StubAgent(_ => Fixture.Walked(Page)), "www.google.de", clients).FindAsync("grain");

        Assert.Equal("https://www.google.de/goto?url=CAESWAHrOzAVblob", clients.Asked.Single().AbsoluteUri);
    }

    /// <summary>A link that already states its address costs nothing, and asking about it would be a request spent.</summary>
    [Fact]
    public async Task FindAsync_ResultsThatAlreadyCarryAnAddress_AreNotAskedAbout()
    {
        var clients = new StubClients();

        await Provider(new StubAgent(_ => Fixture.Walked(Page)), clients: clients).FindAsync("grain");

        Assert.Single(clients.Asked);
    }

    /// <summary>A wrapper that answers anything but a redirect is a link nobody can follow, and not a result.</summary>
    [Fact]
    public async Task FindAsync_WrapperTheEngineDoesNotRedirect_IsNotAResult()
    {
        var found = await Found(Page, new StubClients(HttpStatusCode.OK));

        Assert.Equal(3, found.Count);
        Assert.DoesNotContain(found, result => result.Address.Text.StartsWith("/goto", StringComparison.Ordinal));
    }

    /// <summary>The gap belongs between two follows, not in front of the first — it would be added to every query.</summary>
    [Fact]
    public async Task FindAsync_TheFirstWrapper_IsFollowedWithoutWaitingOutTheGap()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var agent = new StubAgent(_ => Fixture.Walked(Page));
        var answer = await Provider(agent, follow: TimeSpan.FromMinutes(10)).FindAsync("grain", deadline.Token);

        Assert.Equal(4, answer.Value.Count);
    }

    /// <summary>
    /// Two wrappers are two requests to an engine that answers frequency with a challenge, so the second waits.
    /// </summary>
    [Fact]
    public async Task FindAsync_ASecondWrapper_WaitsOutTheGapBeforeItIsFollowed()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var agent = new StubAgent(_ => Fixture.Walked(TwoWrappers));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Provider(agent, follow: TimeSpan.FromMinutes(10)).FindAsync("grain", deadline.Token));
    }

    private static async Task<Errand> Performed()
    {
        var agent = new StubAgent(_ => Fixture.Walked(Page));

        await Provider(agent).FindAsync("grain");

        return agent.Performed!;
    }

    private static async Task<IReadOnlyList<SearchResult>> Found(string html, IHttpClientFactory? clients = null)
        => (await Provider(new StubAgent(_ => Fixture.Walked(html)), clients: clients).FindAsync("grain")).Value;

    /// <summary>
    /// A refusal from this engine names the gap in force, because that number is the one a deployment has to
    /// raise and the only evidence that it is too small.
    /// </summary>
    /// <remarks>
    /// The seventeenth item on the consumer's list: the two minutes this package writes for Google is the
    /// shortest interval that passed in ONE measurement of four queries on clean contexts (2026-09-13), and
    /// nothing has ever measured the rate the engine sustains. Worse, tolerance is per ADDRESS — the
    /// measurement that matters is the deployment's own, not this repository's. So the number cannot be made
    /// true here; what it can be made is visible, and a refusal is the moment to say it.
    /// </remarks>
    [Fact]
    public async Task FindAsync_EngineThatRefused_NamesTheGapInForceAndWhereToRaiseIt()
    {
        var recording = new RecordingLogger();

        var answer = await Provider(
            new StubAgent(_ => Fixture.Refused("are you a robot")),
            logger: recording,
            gap: TimeSpan.FromMinutes(2)).FindAsync("grain");

        Assert.False(answer.IsSuccess);
        Assert.Contains(recording.Warnings, line => line.Contains("00:02:00", StringComparison.Ordinal));
        Assert.Contains(recording.Warnings, line => line.Contains("HostDelays", StringComparison.Ordinal));
    }

    /// <summary>An engine that answered says nothing of the kind, which is what makes the line above a signal.</summary>
    [Fact]
    public async Task FindAsync_EngineThatAnswered_SaysNothingAboutItsPace()
    {
        var recording = new RecordingLogger();

        await Provider(new StubAgent(_ => Fixture.Walked(Page)), logger: recording).FindAsync("grain");

        Assert.DoesNotContain(recording.Warnings, line => line.Contains("HostDelays", StringComparison.Ordinal));
    }

    /// <summary>Keeps the warnings, which is where the gap in force is named.</summary>
    private sealed class RecordingLogger : ILogger<Google>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (logLevel is LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    private static Google Provider(
        IPageAgent agent,
        string domain = "www.google.com",
        IHttpClientFactory? clients = null,
        TimeSpan? follow = null,
        ILogger<Google>? logger = null,
        TimeSpan? gap = null)
        => new(agent,
            clients ?? new StubClients(),
            Options.Create(new GoogleOptions { Domain = domain, FollowDelay = follow ?? TimeSpan.Zero }),
            Options.Create(new SearchOptions()),
            Options.Create(new PolitenessOptions
            {
                HostDelays = gap is { } named
                    ? new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase) { [domain] = named }
                    : []
            }),
            logger ?? NullLogger<Google>.Instance);

    /// <summary>Answers a wrapper the way the engine does, and remembers every wrapper it was asked about.</summary>
    private sealed class StubClients(HttpStatusCode status = HttpStatusCode.Found) : IHttpClientFactory
    {
        private readonly StubHandler _handler = new(status);

        public IReadOnlyList<Uri> Asked => _handler.Asked;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        private readonly List<Uri> _asked = [];

        public IReadOnlyList<Uri> Asked => _asked;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _asked.Add(request.RequestUri!);

            var reply = new HttpResponseMessage(status);

            if (status is HttpStatusCode.Found)
                reply.Headers.Location = new Uri($"https://unwrapped.example/{_asked.Count}");

            return Task.FromResult(reply);
        }
    }
}
