using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

        Assert.Empty(await Engine(reader).FindAsync("   "));
    }

    /// <summary>
    /// Reading answers with failure as data while a provider is contracted to throw. The exception names the
    /// engine because a caller running several of them needs to know which wall it hit — and a challenge page
    /// is thrown as the refusal the search rests an engine over.
    /// </summary>
    [Fact]
    public async Task FindAsync_PageRefused_ThrowsNamingTheEngineAndCarryingTheFailure()
    {
        var reader = new StubReader(_ => Fixture.Refused(FailureKind.Blocked, "consent wall"));

        var refusal = await Assert.ThrowsAsync<PageRefusedException>(() => Engine(reader).FindAsync("grain"));

        Assert.Equal("Elsewhere", refusal.Provider);
        Assert.Equal(FailureKind.Blocked, refusal.Failure.Kind);
    }

    /// <summary>A 429 is the engine saying "later" in the status line, and is a refusal for the same reason a challenge is.</summary>
    [Fact]
    public async Task FindAsync_TooManyRequests_IsARefusal()
    {
        var reader = new StubReader(_ => Fixture.Refused(FailureKind.Navigation, "too many requests", status: 429));

        var refusal = await Assert.ThrowsAsync<PageRefusedException>(() => Engine(reader).FindAsync("grain"));

        Assert.Equal(429, refusal.Failure.StatusCode);
    }

    /// <summary>
    /// A timeout says nothing about the engine's patience: resting it over one would leave a working engine out
    /// of the next searches over a hiccup on this side. It is still thrown, because an empty list would read as
    /// "nothing found" — just not as the refusal the search reacts to.
    /// </summary>
    [Theory]
    [InlineData(FailureKind.Timeout)]
    [InlineData(FailureKind.BrowserUnavailable)]
    [InlineData(FailureKind.Proxy)]
    [InlineData(FailureKind.Navigation)]
    public async Task FindAsync_PageThatCouldNotBeRead_ThrowsButNotAsARefusal(FailureKind kind)
    {
        var reader = new StubReader(_ => Fixture.Refused(kind, "our side"));

        var unread = await Assert.ThrowsAsync<PageUnreadException>(() => Engine(reader).FindAsync("grain"));

        Assert.IsNotAssignableFrom<SearchRefusedException>(unread);
        Assert.Equal(kind, unread.Failure.Kind);
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

    private static async Task<IReadOnlyList<SearchResult>> Found(string page)
        => await Engine(new StubReader(_ => Fixture.Read(page))).FindAsync("grain");

    private static Elsewhere Engine(IPageReader reader, ILogger? logger = null)
        => new(reader, logger ?? NullLogger.Instance);

    /// <summary>An engine that supplies only an address and a parser, so the base carries everything else.</summary>
    private sealed class Elsewhere(IPageReader pages, ILogger logger) : BrowserSearch(pages, logger)
    {
        public override string Name => "Elsewhere";

        protected override string Domain => "search.example";

        protected override Link AddressOf(string query)
            => Link.Of($"https://search.example/find?q={Uri.EscapeDataString(query)}");

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
