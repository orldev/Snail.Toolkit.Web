using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// Covers merging across providers: what deduplication means, and what an empty answer is allowed to mean.
/// </summary>
public class WebSearchTests
{
    /// <summary>
    /// Deduplication is the reason several providers are worth asking at once, and it happens on
    /// <see cref="Link"/> equality — the same page arrives from two of them in two shapes.
    /// </summary>
    [Fact]
    public async Task FindAsync_TheSamePageFromTwoProviders_ComesBackOnce()
    {
        var search = Search(
            Answering("A", "http://Example.com/grain/"),
            Answering("B", "https://example.com/grain?utm_source=b"));

        var outcome = await search.FindAsync(["grain"]);

        Assert.Single(outcome.Results);
        Assert.Equal(2, outcome.Attempts.Count);
    }

    [Fact]
    public async Task FindAsync_DistinctPages_AreAllKept()
    {
        var search = Search(Answering("A", "https://example.com/a"), Answering("B", "https://example.com/b"));

        Assert.Equal(2, (await search.FindAsync(["grain"])).Results.Count);
    }

    /// <summary>One provider failing must not take the answer with it.</summary>
    [Fact]
    public async Task FindAsync_OneProviderThrows_TheOthersStillAnswer()
    {
        var search = Search(Answering("A", "https://example.com/a"), Faulting("B"));

        var outcome = await search.FindAsync(["grain"]);

        Assert.Single(outcome.Results);
        Assert.Single(outcome.Attempts, attempt => attempt.Failure?.Kind is SearchFailureKind.Faulted);
        Assert.True(outcome.IsAnswered);
    }

    /// <summary>
    /// The distinction the whole outcome type exists for: nothing matched reads exactly like nobody answered,
    /// and the two call for opposite reactions.
    /// </summary>
    [Fact]
    public async Task FindAsync_EveryProviderAnsweredWithNothing_ReportsAnAnsweredSearch()
    {
        var outcome = await Search(Empty("A"), Empty("B")).FindAsync(["grain"]);

        Assert.False(outcome.HasResults);
        Assert.True(outcome.IsAnswered);
    }

    [Fact]
    public async Task FindAsync_EveryProviderFailed_ReportsAnUnansweredSearch()
    {
        var outcome = await Search(Faulting("A"), Faulting("B")).FindAsync(["grain"]);

        Assert.False(outcome.HasResults);
        Assert.False(outcome.IsAnswered);
    }

    [Fact]
    public async Task FindAsync_ProviderThatFailed_CarriesTheMessageItThrew()
    {
        var outcome = await Search(Faulting("A")).FindAsync(["grain"]);

        Assert.Equal("provider is down", outcome.Attempts[0].Failure?.Message);
    }

    [Fact]
    public async Task FindAsync_EveryQueryAgainstEveryProvider_IsAttempted()
    {
        var outcome = await Search(Empty("A"), Empty("B")).FindAsync(["grain", "barley"]);

        Assert.Equal(4, outcome.Attempts.Count);
        Assert.Equal(["barley", "barley", "grain", "grain"], outcome.Attempts.Select(a => a.Query).Order());
    }

    [Fact]
    public async Task StreamAsync_EachAttempt_ArrivesAsItFinishes()
    {
        var seen = new List<SearchAttempt>();

        await foreach (var attempt in Search(Empty("A"), Empty("B")).StreamAsync(["grain"]))
            seen.Add(attempt);

        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public async Task FindAsync_NoQueries_AttemptsNothing()
        => Assert.Empty((await Search(Empty("A")).FindAsync([])).Attempts);

    [Fact]
    public async Task FindAsync_NoProvidersRegistered_ReportsAnUnansweredSearch()
    {
        var outcome = await Search().FindAsync(["grain"]);

        Assert.Empty(outcome.Attempts);
        Assert.False(outcome.IsAnswered);
    }

    [Fact]
    public async Task FindAsync_CancelledToken_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Search(Empty("A")).FindAsync(["grain"], cancellation.Token));
    }

    private static WebSearch Search(params ISearchProvider[] providers)
        => new(providers, Options.Create(new SearchOptions()), NullLogger<WebSearch>.Instance);

    private static ISearchProvider Answering(string name, params string[] urls)
        => new StubProvider(name, _ => [.. urls.Select(url => new SearchResult
        {
            Title = url,
            Address = Link.Of(url),
            Snippet = string.Empty,
            Provider = name
        })]);

    private static ISearchProvider Empty(string name) => new StubProvider(name, _ => []);

    private static ISearchProvider Faulting(string name)
        => new StubProvider(name, _ => throw new InvalidOperationException("provider is down"));

    private sealed class StubProvider(string name, Func<string, IReadOnlyList<SearchResult>> answer) : ISearchProvider
    {
        public string Name => name;

        public Task<IReadOnlyList<SearchResult>> FindAsync(string query, CancellationToken ct = default)
            => Task.FromResult(answer(query));
    }
}
