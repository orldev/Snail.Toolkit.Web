using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;

namespace Snail.Toolkit.Web.Search.Browser.Tests;

/// <summary>
/// Covers the behaviour every browser-backed engine inherits, driven through an engine that is not Google.
/// </summary>
/// <remarks>
/// Google is only the first engine that refuses a plain HTTP client — Bing, Yandex, Brave and Baidu do the same.
/// These tests exist to prove that what they share is actually shared: the fixture engine below supplies nothing
/// but an address and a parser, and everything else has to work for it unchanged.
/// </remarks>
public class BrowserSearchTests
{
    [Fact]
    public async Task FindAsync_TheEnginesOwnAddress_IsWhatIsRead()
    {
        var reader = new StubReader(_ => Fixture.Read(string.Empty));

        await Engine(reader).FindAsync("grain & barley");

        Assert.Equal("https://search.example/find?q=grain%20%26%20barley", reader.Requested?.Address?.AbsoluteUri);
    }

    [Fact]
    public async Task FindAsync_BlankQuery_ReadsNothingAtAll()
    {
        var reader = new StubReader(_ => throw new InvalidOperationException("should not be asked"));

        Assert.Empty((await Engine(reader).FindAsync("   ")).Value);
    }

    /// <summary>
    /// A challenge page comes back as the refusal the search rests an engine over, and names the engine,
    /// because a caller running several of them needs to know which wall it hit.
    /// </summary>
    [Fact]
    public async Task FindAsync_PageRefused_IsARefusalNamingTheEngineAndTheWall()
    {
        var reader = new StubReader(_ => Fixture.Refused(FailureKind.Blocked, "consent wall"));

        var answer = await Engine(reader).FindAsync("grain");

        Assert.Equal(SearchFailureKind.Refused, answer.Failure?.Kind);
        Assert.Contains("Elsewhere", answer.Failure!.Message!, StringComparison.Ordinal);
        Assert.Contains("consent wall", answer.Failure.Message!, StringComparison.Ordinal);
    }

    /// <summary>A 429 is the engine saying "later" in the status line, and is a refusal for the same reason a challenge is.</summary>
    [Fact]
    public async Task FindAsync_TooManyRequests_IsARefusal()
    {
        var reader = new StubReader(_ => Fixture.Refused(FailureKind.Navigation, "too many requests", status: 429));

        var answer = await Engine(reader).FindAsync("grain");

        Assert.Equal(SearchFailureKind.Refused, answer.Failure?.Kind);
        Assert.Contains("429", answer.Failure!.Message!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A timeout says nothing about the engine's patience: resting it over one would leave a working engine out
    /// of the next searches over a hiccup on this side. It is still a failure rather than an empty answer,
    /// because an empty list reads as "nothing found" — just not as the refusal the search reacts to.
    /// </summary>
    [Theory]
    [InlineData(FailureKind.Timeout)]
    [InlineData(FailureKind.BrowserUnavailable)]
    [InlineData(FailureKind.Proxy)]
    [InlineData(FailureKind.Navigation)]
    public async Task FindAsync_PageThatCouldNotBeRead_IsAFaultAndNotARefusal(FailureKind kind)
    {
        var reader = new StubReader(_ => Fixture.Refused(kind, "our side"));

        var answer = await Engine(reader).FindAsync("grain");

        Assert.Equal(SearchFailureKind.Faulted, answer.Failure?.Kind);
        Assert.Contains(kind.ToString(), answer.Failure!.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindAsync_LinksBackToTheEngine_AreDropped()
    {
        var results = await Found("https://search.example/settings\nhttps://example.com/grain");

        Assert.Equal("https://example.com/grain", Assert.Single(results).Address.Canonical);
    }

    [Fact]
    public async Task FindAsync_TheEnginesServiceHosts_AreDropped()
        => Assert.Empty(await Found("https://accounts.search.example/signin\nhttps://support.search.example/help"));

    [Fact]
    public async Task FindAsync_SomethingThatIsNotAnAddress_IsDropped()
        => Assert.Empty(await Found("not a url at all"));

    /// <summary>
    /// A layout the parser can no longer follow looks exactly like a quiet day: a page that loaded, and no
    /// results. The warning is what tells the two apart before the metrics do.
    /// </summary>
    [Fact]
    public async Task FindAsync_PageThatParsedIntoNothing_WarnsThatTheLayoutMayHaveChanged()
    {
        var logger = new RecordingLogger();
        var reader = new StubReader(_ => Fixture.Read("we redesigned everything"));

        await Engine(reader, logger).FindAsync("grain");

        Assert.Contains(logger.Warnings, warning => warning.Contains("Elsewhere", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindAsync_PageThatYieldedResults_WarnsAboutNothing()
    {
        var logger = new RecordingLogger();

        await Engine(new StubReader(_ => Fixture.Read("https://example.com/grain")), logger).FindAsync("grain");

        Assert.Empty(logger.Warnings);
    }

    /// <summary>One engine may not crowd the rest out of the answer, however much it found.</summary>
    /// <remarks>
    /// The ceiling was read by the two engines that fetch over HTTP and by neither of the two that drive a
    /// browser, and a browser engine is the one that answers with most: measured on 2026-09-12, Brave returned
    /// a hundred links to one query while the setting stood at thirty.
    /// </remarks>
    [Fact]
    public async Task FindAsync_EngineThatFoundMoreThanTheCeiling_AnswersWithTheCeiling()
    {
        var page = string.Join('\n', Enumerable.Range(0, 50).Select(index => $"https://example.com/{index}"));

        var answer = await Engine(new StubReader(_ => Fixture.Read(page)), maxResults: 5).FindAsync("grain");

        Assert.Equal(5, answer.Value.Count);
        Assert.Equal("https://example.com/0", answer.Value[0].Address.Text);
    }

    /// <summary>
    /// An address that is not absolute carries no host, so the outbound filter reads it as the engine's own
    /// and drops it. An engine whose page wraps its links has to turn them into addresses before this point.
    /// </summary>
    /// <remarks>
    /// This is what Google did on 2026-09-14: a megabyte of markup, seven wrapped links, and an answer of no
    /// results that looked exactly like a quiet day.
    /// </remarks>
    [Fact]
    public async Task FindAsync_AddressThatIsNotAbsolute_IsNotAResult()
        => Assert.Empty(await Found("/goto?url=CAESWAHrOzAVblob"));

    private static async Task<IReadOnlyList<SearchResult>> Found(string page)
        => (await Engine(new StubReader(_ => Fixture.Read(page))).FindAsync("grain")).Value;

    private static Elsewhere Engine(IPageReader reader, ILogger? logger = null, int maxResults = 30)
        => new(reader,
            Options.Create(new SearchOptions { MaxResultsPerProvider = maxResults }),
            logger ?? NullLogger.Instance);

    /// <summary>An engine that supplies only a fetch and a parser, so the base carries everything else.</summary>
    private sealed class Elsewhere(IPageReader pages, IOptions<SearchOptions> search, ILogger logger)
        : BrowserSearch(search, logger)
    {
        public override string Name => "Elsewhere";

        protected override string Domain => "search.example";

        protected override Task<PageOutcome> AnsweredAsync(string query, CancellationToken ct)
            => pages.ReadAsync(
                Link.Of($"https://search.example/find?q={Uri.EscapeDataString(query)}"),
                ct);

        protected override IEnumerable<SearchResult> Parsed(string html)
            => html
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => new SearchResult
                {
                    Title = line,
                    Address = Link.Of(line),
                    Snippet = string.Empty,
                    Provider = Name
                });
    }

    private sealed class RecordingLogger : ILogger
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
            if (logLevel is LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
