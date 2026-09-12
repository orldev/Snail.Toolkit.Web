using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// Covers merging across providers: what deduplication means, what an empty answer is allowed to mean, what a
/// refusal does to the next search, and how a batch is spread over the providers.
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

    /// <summary>
    /// A refusal is not a fault: one is the engine asking to be left alone, the other is a defect on this side,
    /// and an outcome that called both "faulted" would leave the caller unable to react to either.
    /// </summary>
    [Fact]
    public async Task FindAsync_ProviderRefused_IsRecordedAsRefusedNotFaulted()
    {
        var outcome = await Search(Answering("A", "https://example.com/a"), Refusing("B")).FindAsync(["grain"]);

        var refused = Assert.Single(outcome.Attempts, attempt => attempt.Provider == "B");

        Assert.Equal(SearchFailureKind.Refused, refused.Failure?.Kind);
        Assert.Equal("challenge page", refused.Failure?.Message);
        Assert.False(refused.IsAnswered);
        Assert.True(outcome.IsAnswered);
    }

    /// <summary>Asking a challenged engine again at once is how a pause becomes a ban.</summary>
    [Fact]
    public async Task FindAsync_AfterARefusal_LeavesThatProviderOutOfTheNextSearch()
    {
        var search = Search(Answering("A", "https://example.com/a"), Refusing("B"));

        await search.FindAsync(["grain"]);
        var second = await search.FindAsync(["barley"]);

        Assert.Equal("A", Assert.Single(second.Attempts).Provider);
    }

    /// <summary>
    /// A fault is ours — a parser that lost the markup, a socket that died — and resting the engine over it
    /// would hide exactly the thing that needs fixing.
    /// </summary>
    [Fact]
    public async Task FindAsync_AfterAFault_StillAsksThatProviderNextTime()
    {
        var search = Search(Answering("A", "https://example.com/a"), Faulting("B"));

        await search.FindAsync(["grain"]);
        var second = await search.FindAsync(["barley"]);

        Assert.Equal(2, second.Attempts.Count);
    }

    /// <summary>
    /// When every provider rests, the honest answer is that nobody was asked — not an answered search that
    /// happened to find nothing.
    /// </summary>
    [Fact]
    public async Task FindAsync_EveryProviderResting_AsksNobodyAndReportsAnUnansweredSearch()
    {
        var search = Search(Refusing("A"), Refusing("B"));

        await search.FindAsync(["grain"]);
        var second = await search.FindAsync(["barley"]);

        Assert.Empty(second.Attempts);
        Assert.False(second.IsAnswered);
    }

    /// <summary>
    /// The rest is the engine's to end: once it has run out the provider is asked again, and an answer — even
    /// an empty one — puts it back in the set for good.
    /// </summary>
    [Fact]
    public async Task FindAsync_ProviderThatAnswersOnceItsRestIsOver_IsBackInTheSet()
    {
        var clock = new Clock(new DateTimeOffset(2026, 9, 12, 20, 0, 0, TimeSpan.Zero));
        var refusalsLeft = 1;
        var recovering = new StubProvider("B", _ =>
        {
            if (refusalsLeft-- > 0)
                throw new SearchRefusedException("B", "challenge page");

            return [];
        });
        var search = Search(Cooldown(clock), Answering("A", "https://example.com/a"), recovering);

        await search.FindAsync(["grain"]);
        clock.Now += TimeSpan.FromMinutes(5);
        var second = await search.FindAsync(["barley"]);
        var third = await search.FindAsync(["rye"]);

        Assert.Equal(2, second.Attempts.Count);
        Assert.Equal(2, third.Attempts.Count);
    }

    /// <summary>
    /// With no more providers than the round holds, every query goes to every one of them — the search as it
    /// always was, and what a deployment with one or two engines keeps getting.
    /// </summary>
    [Fact]
    public async Task FindAsync_RoundNoSmallerThanTheSet_AsksEveryProviderEveryQuery()
    {
        var outcome = await Search(Empty("A"), Empty("B")).FindAsync(["grain", "barley"]);

        Assert.Equal(4, outcome.Attempts.Count);
        Assert.Equal(["barley", "barley", "grain", "grain"], outcome.Attempts.Select(a => a.Query).Order());
    }

    /// <summary>
    /// The round is what the phase is for: with four engines and two per query, a query lands on two of them
    /// and the next query on the other two, so nobody is asked twice in a row while the batch still reaches
    /// all four.
    /// </summary>
    [Fact]
    public async Task FindAsync_MoreProvidersThanTheRound_SendsEachQueryToTheNextOnesInTurn()
    {
        var search = Search(Empty("A"), Empty("B"), Empty("C"), Empty("D"));

        var outcome = await search.FindAsync(["grain", "barley"]);

        Assert.Equal(["A", "B"], AskedFor(outcome, "grain"));
        Assert.Equal(["C", "D"], AskedFor(outcome, "barley"));
    }

    /// <summary>Over a series the round comes back to where it started, and every engine carries the same share.</summary>
    [Fact]
    public async Task FindAsync_OverASeriesOfQueries_EveryProviderCarriesAnEqualShare()
    {
        var search = Search(Empty("A"), Empty("B"), Empty("C"), Empty("D"));

        var first = await search.FindAsync(["grain", "barley", "rye"]);
        var later = await search.FindAsync(["oats"]);

        var shares = first.Attempts.Concat(later.Attempts)
            .GroupBy(attempt => attempt.Provider)
            .Select(share => share.Count());

        Assert.Equal([2, 2, 2, 2], shares);
    }

    /// <summary>A resting engine is skipped by the round rather than counted as a turn: its share goes to the ones awake.</summary>
    [Fact]
    public async Task FindAsync_RestingProvider_IsSkippedByTheRound()
    {
        var search = Search(Refusing("A"), Empty("B"), Empty("C"), Empty("D"));

        await search.FindAsync(["grain"]);
        var outcome = await search.FindAsync(["barley", "rye"]);

        Assert.Equal(4, outcome.Attempts.Count);
        Assert.DoesNotContain(outcome.Attempts, attempt => attempt.Provider == "A");
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

    /// <summary>
    /// A provider that costs nothing to ask — a local index — stands outside the round: it joins every query
    /// beside the engines the round spares, or its answer would be a matter of luck.
    /// </summary>
    [Fact]
    public async Task FindAsync_ProviderAlwaysAsked_JoinsEveryQueryBesideTheRound()
    {
        var search = Search(
            new SearchOptions { AlwaysAsked = ["E"] },
            Empty("A"), Empty("B"), Empty("C"), Empty("D"), Empty("E"));

        var outcome = await search.FindAsync(["grain", "barley"]);

        Assert.Equal(["A", "B", "E"], AskedFor(outcome, "grain"));
        Assert.Equal(["C", "D", "E"], AskedFor(outcome, "barley"));
    }

    private static IEnumerable<string> AskedFor(SearchOutcome outcome, string query)
        => outcome.Attempts.Where(attempt => attempt.Query == query).Select(attempt => attempt.Provider).Order();

    private static WebSearch Search(params ISearchProvider[] providers)
        => Search(Cooldown(TimeProvider.System), providers);

    private static WebSearch Search(ISearchCooldown cooldown, params ISearchProvider[] providers)
        => new(providers, cooldown, Options.Create(new SearchOptions()), NullLogger<WebSearch>.Instance);

    private static WebSearch Search(SearchOptions options, params ISearchProvider[] providers)
        => new(providers, Cooldown(TimeProvider.System), Options.Create(options), NullLogger<WebSearch>.Instance);

    private static SearchCooldown Cooldown(TimeProvider clock)
        => new(Options.Create(new SearchOptions()), clock);

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

    private static ISearchProvider Refusing(string name)
        => new StubProvider(name, _ => throw new SearchRefusedException(name, "challenge page"));

    private sealed class StubProvider(string name, Func<string, IReadOnlyList<SearchResult>> answer) : ISearchProvider
    {
        public string Name => name;

        public Task<IReadOnlyList<SearchResult>> FindAsync(string query, CancellationToken ct = default)
            => Task.FromResult(answer(query));
    }
}
