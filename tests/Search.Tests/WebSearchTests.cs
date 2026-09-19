using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Text;
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

        Assert.Equal("A", Assert.Single(second.Attempts, attempt => attempt.IsAnswered).Provider);
        Assert.Equal(
            SearchFailureKind.Resting,
            Assert.Single(second.Attempts, attempt => attempt.Provider == "B").Failure!.Kind);
    }

    /// <summary>
    /// The rest starts the moment the engine refuses, not at the next search: a batch planned before the
    /// refusal must not carry on asking the engine for every query left in it.
    /// </summary>
    /// <remarks>
    /// MEASURED on 2026-09-20 before the fix: five queries, one refusing engine — five requests to it, and a
    /// rest grown to the one-hour ceiling instead of five minutes.
    /// </remarks>
    [Fact]
    public async Task FindAsync_ProviderRefusesMidBatch_IsNotAskedAgainInThatBatch()
    {
        var asked = 0;
        var refusing = new StubProvider("B", _ =>
        {
            asked++;

            return SearchAnswer.Failed(new SearchFailure(SearchFailureKind.Refused, "challenge page"));
        });
        var search = Search(new SearchOptions { MaxDegreeOfParallelism = 1 }, refusing);

        var outcome = await search.FindAsync(["q1", "q2", "q3", "q4", "q5"]);

        Assert.Equal(1, asked);
        Assert.Equal(4, outcome.Attempts.Count(attempt => attempt.Failure?.Kind is SearchFailureKind.Resting));
    }

    /// <summary>
    /// A caller that has what it needs stops reading, and the attempts still running are then work nobody
    /// wants: they must be told so, not left holding a browser and a host's turn.
    /// </summary>
    /// <remarks>
    /// MEASURED on 2026-09-20 before the fix: the caller was held for the fan-out's five-second grace and the
    /// slow attempt was never cancelled — the attempts ran under the budget's token and the fan-out's was
    /// thrown away. The wait below is shorter than that grace, so this cannot pass by outlasting it.
    /// </remarks>
    [Fact]
    public async Task StreamAsync_CallerStopsReading_CancelsTheAttemptsStillRunning()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var search = Search(Empty("A"), new Slow("B", cancelled));

        await foreach (var _ in search.StreamAsync(["grain"]))
            break;

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// A place in the round is worth something, and an engine that has just failed cannot use it: the query
    /// after it goes to an engine that can answer, while the failing one is still asked once so the failure
    /// stays visible.
    /// </summary>
    /// <remarks>
    /// MEASURED by the consumer on 2026-09-16: four engines, two per query — the round handed both places to
    /// one engine resting after a refusal and one that was faulting, and never asked the engine that answers
    /// this query well. The search came back empty about a subject the third engine has plenty on.
    /// </remarks>
    [Fact]
    public async Task FindAsync_ProviderThatFaulted_GivesUpItsPlaceInTheRoundToOneThatCanAnswer()
    {
        var asked = 0;
        var faulting = new StubProvider("A", _ =>
        {
            asked++;

            throw new InvalidOperationException("parser no longer follows the markup");
        });
        var options = new SearchOptions { ProvidersPerQuery = 2, MaxDegreeOfParallelism = 1 };
        var search = Search(options, faulting, Answering("B", "https://example.com/b"), Answering("C", "https://example.com/c"));

        var outcome = await search.FindAsync(["q1", "q2", "q3"]);

        Assert.Equal(1, asked);
        Assert.Equal(["B", "C"], AskedFor(outcome, "q3"));
    }

    /// <summary>
    /// A resting engine gives up its place for the same reason, and that is the other half of the same
    /// measurement: its place went to nobody, so a query with two places got one answer.
    /// </summary>
    [Fact]
    public async Task FindAsync_ProviderThatRefused_GivesUpItsPlaceInTheRoundToOneThatCanAnswer()
    {
        var options = new SearchOptions { ProvidersPerQuery = 2, MaxDegreeOfParallelism = 1 };
        var search = Search(
            options,
            Refusing("A"),
            Answering("B", "https://example.com/b"),
            Answering("C", "https://example.com/c"));

        var outcome = await search.FindAsync(["q1", "q2"]);

        Assert.Equal(["B", "C"], AskedFor(outcome, "q2"));
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

        Assert.Equal(
            [SearchFailureKind.Resting, SearchFailureKind.Resting],
            second.Attempts.Select(attempt => attempt.Failure!.Kind));

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
                return SearchAnswer.Failed(new SearchFailure(SearchFailureKind.Refused, "challenge page"));

            return SearchAnswer.Success([]);
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

        var asked = outcome.Attempts.Where(attempt => attempt.Failure?.Kind != SearchFailureKind.Resting).ToList();

        Assert.Equal(4, asked.Count);
        Assert.DoesNotContain(asked, attempt => attempt.Provider == "A");
    }

    /// <summary>
    /// A provider left out is a fact the caller can state, not only one the log holds: the attempt says it was
    /// not asked and when its rest ends.
    /// </summary>
    /// <remarks>
    /// "Every engine answered and nothing matched" and "the engines that answer this kind of question are all
    /// resting" arrived as the same empty outcome, and they call for opposite reactions — the first is about
    /// the query, the second about the search.
    /// </remarks>
    [Fact]
    public async Task FindAsync_RestingProvider_SaysSoInTheAttemptsWithTheInstantItEnds()
    {
        var clock = new Clock(new DateTimeOffset(2026, 9, 12, 20, 0, 0, TimeSpan.Zero));
        var search = Search(Cooldown(clock), Refusing("A"), Empty("B"));

        await search.FindAsync(["grain"]);
        var second = await search.FindAsync(["barley"]);

        var unasked = Assert.Single(second.Attempts, attempt => attempt.Provider == "A");

        Assert.Equal(SearchFailureKind.Resting, unasked.Failure!.Kind);
        Assert.Equal(clock.Now.AddMinutes(5), unasked.Failure.OpensAt);
        Assert.False(unasked.IsAnswered);
    }

    /// <summary>One entry per provider and query, like every other attempt, so a report counts them alike.</summary>
    [Fact]
    public async Task FindAsync_RestingProviderAndTwoQueries_SaysSoOncePerQuery()
    {
        var search = Search(Refusing("A"), Empty("B"));

        await search.FindAsync(["grain"]);
        var second = await search.FindAsync(["barley", "rye"]);

        Assert.Equal(
            ["barley", "rye"],
            second.Attempts.Where(attempt => attempt.Provider == "A").Select(attempt => attempt.Query).Order());
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

    /// <summary>Markup too deep to read says nothing about how often the engine will answer.</summary>
    /// <remarks>
    /// A refusal rests the engine; a fault does not, and neither does this. The kind sits beside
    /// <see cref="SearchFailureKind.Faulted"/> in what it costs and apart from it in what it means, so the
    /// cheapest way for it to go wrong is to be folded into the refusal arm by someone reading the enum.
    /// </remarks>
    [Fact]
    public async Task FindAsync_ProviderThatAnsweredMarkupTooDeepToRead_IsNotRested()
    {
        var cooldown = Cooldown(new Clock(DateTimeOffset.UnixEpoch));

        await Search(cooldown, Unreadable("A")).FindAsync(["grain"]);

        Assert.Null(cooldown.RestingUntil("A"));
    }

    /// <summary>And it is not an answer: nobody read that page, so nothing is known about the query.</summary>
    [Fact]
    public async Task FindAsync_EveryProviderAnsweredMarkupTooDeepToRead_IsNotAnAnsweredSearch()
    {
        var outcome = await Search(Unreadable("A"), Unreadable("B")).FindAsync(["grain"]);

        Assert.False(outcome.IsAnswered);
    }

    private static ISearchProvider Unreadable(string name)
        => new StubProvider(name, _ => SearchAnswer.Failed(
            SearchFailure.Unreadable(name, new MarkupTooDeepException(1_000))));

    /// <summary>
    /// A budget is the caller saying what a query is worth, and the gate already knows what each engine costs.
    /// </summary>
    /// <remarks>
    /// The engine here is paced at five minutes and has just been asked, so its gate cannot open inside a
    /// budget of one second. Nothing about the engine is guessed: the gap is the one the deployment named and
    /// the instant is the gate's own answer.
    /// </remarks>
    [Fact]
    public async Task FindAsync_BudgetShorterThanTheGateTheEngineWaitsAt_DoesNotAskItAndNamesWhenItOpens()
    {
        var gate = Paced(("slow.example", TimeSpan.FromMinutes(5)));
        await Turned(gate, "https://slow.example/search");

        var slow = new StubProvider("Slow", _ => SearchAnswer.Success([]), host: "slow.example");

        var outcome = await Timed(gate, TimeProvider.System, slow).FindAsync(["grain"], TimeSpan.FromSeconds(1));

        var attempt = Assert.Single(outcome.Attempts);

        Assert.Equal(SearchFailureKind.OutOfTime, attempt.Failure!.Kind);
        Assert.NotNull(attempt.Failure.OpensAt);
        Assert.False(slow.Asked);
    }

    /// <summary>
    /// Without a budget the engine is asked whenever one request of it can finish, which is what makes the
    /// answer above a choice: the bound is then <see cref="SearchOptions.Timeout"/>, not the caller's.
    /// </summary>
    [Fact]
    public async Task FindAsync_NoBudget_AsksTheEngineWhoseGateOpensWithinOneRequest()
    {
        var gate = Paced(("slow.example", TimeSpan.FromMinutes(5)));
        await Turned(gate, "https://slow.example/search");

        var slow = new StubProvider("Slow", _ => SearchAnswer.Success([]), host: "slow.example");
        var options = new SearchOptions { Timeout = TimeSpan.FromMinutes(10) };

        await Built(Cooldown(TimeProvider.System), gate, TimeProvider.System, options, [slow]).FindAsync(["grain"]);

        Assert.True(slow.Asked);
    }

    /// <summary>
    /// An engine whose host opens later than one request of it may take cannot answer this search, whatever it
    /// is asked — so it is not asked, and the caller hears when it opens instead of waiting to be timed out.
    /// </summary>
    /// <remarks>
    /// Read on 2026-09-20: the caller was held for the whole <see cref="SearchOptions.Timeout"/> at the gate
    /// and then told the engine had faulted, while the gate had known the answer before the request began.
    /// Google, whose host keeps minutes between requests, met this on every second query.
    /// </remarks>
    [Fact]
    public async Task FindAsync_NoBudget_GateShutLongerThanOneRequestMayTake_DoesNotAskItAndNamesWhenItOpens()
    {
        var gate = Paced(("slow.example", TimeSpan.FromMinutes(5)));
        await Turned(gate, "https://slow.example/search");

        var slow = new StubProvider("Slow", _ => SearchAnswer.Success([]), host: "slow.example");

        var outcome = await Timed(gate, TimeProvider.System, slow).FindAsync(["grain"]);

        var attempt = Assert.Single(outcome.Attempts);

        Assert.Equal(SearchFailureKind.OutOfTime, attempt.Failure!.Kind);
        Assert.NotNull(attempt.Failure.OpensAt);
        Assert.False(slow.Asked);
    }

    /// <summary>
    /// A provider that names no host is asked whatever the budget: this library knows nothing about its cost,
    /// and refusing to ask on a guess is worse than paying it.
    /// </summary>
    [Fact]
    public async Task FindAsync_WithABudget_StillAsksAProviderThatNamesNoHost()
    {
        var gate = Paced(("slow.example", TimeSpan.FromMinutes(5)));
        await Turned(gate, "https://slow.example/search");

        var local = new StubProvider("Corpus", _ => SearchAnswer.Success([]));

        await Timed(gate, TimeProvider.System, local).FindAsync(["grain"], TimeSpan.FromSeconds(1));

        Assert.True(local.Asked);
    }

    /// <summary>A budget that runs out while an engine is still answering is the budget's doing, not the caller's.</summary>
    [Fact]
    public async Task FindAsync_BudgetRunningOutWhileAProviderAnswers_SaysOutOfTimeRatherThanCanceled()
    {
        var outcome = await Timed(Open(), TimeProvider.System, new Waiting("Slow"))
            .FindAsync(["grain"], TimeSpan.FromMilliseconds(100));

        Assert.Equal(SearchFailureKind.OutOfTime, Assert.Single(outcome.Attempts).Failure!.Kind);
    }

    /// <summary>A caller that changes its mind mid-search is answered with the cancellation, budget or not.</summary>
    /// <remarks>
    /// Cancellation passes through this toolkit rather than being translated, and a budget is the one thing
    /// that could have started translating it: reported as an attempt, a cancelled search would come back
    /// looking like a search that ran and found nothing.
    /// </remarks>
    [Fact]
    public async Task FindAsync_CallerCancellingMidSearch_PassesTheCancellationThrough()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Timed(Open(), TimeProvider.System, new Waiting("Slow"))
                .FindAsync(["grain"], TimeSpan.FromSeconds(30), cancellation.Token));
    }

    /// <summary>A gate with the gaps this deployment named, and one turn taken at a host to shut it.</summary>
    private static HostPoliteness Paced(params (string Host, TimeSpan Gap)[] gentler)
        => new(Options.Create(new PolitenessOptions
        {
            HostDelays = gentler.ToDictionary(named => named.Host, named => named.Gap, StringComparer.OrdinalIgnoreCase)
        }));

    private static async Task Turned(IPoliteness gate, string address)
    {
        await using var turn = await gate.EnterAsync(new Uri(address));
    }

    private static IEnumerable<string> AskedFor(SearchOutcome outcome, string query)
        => outcome.Attempts.Where(attempt => attempt.Query == query).Select(attempt => attempt.Provider).Order();

    private static WebSearch Search(params ISearchProvider[] providers)
        => Search(Cooldown(TimeProvider.System), providers);

    private static WebSearch Search(ISearchCooldown cooldown, params ISearchProvider[] providers)
        => Built(cooldown, Open(), TimeProvider.System, new SearchOptions(), providers);

    private static WebSearch Search(SearchOptions options, params ISearchProvider[] providers)
        => Built(Cooldown(TimeProvider.System), Open(), TimeProvider.System, options, providers);

    /// <summary>A search whose clock and gate the test holds, which is what a budget is measured against.</summary>
    private static WebSearch Timed(IPoliteness gate, TimeProvider clock, params ISearchProvider[] providers)
        => Built(Cooldown(clock), gate, clock, new SearchOptions(), providers);

    private static WebSearch Built(
        ISearchCooldown cooldown,
        IPoliteness politeness,
        TimeProvider clock,
        SearchOptions options,
        ISearchProvider[] providers)
        => new(
            providers,
            cooldown,
            politeness,
            clock,
            Options.Create(options),
            NullLogger<WebSearch>.Instance);

    /// <summary>A gate nobody has spoken to, which is what every host looks like until it is asked.</summary>
    private static HostPoliteness Open() => new(Options.Create(new PolitenessOptions()));

    private static SearchCooldown Cooldown(TimeProvider clock)
        => new(Options.Create(new SearchOptions()), clock);

    private static ISearchProvider Answering(string name, params string[] urls)
        => new StubProvider(name, _ => SearchAnswer.Success([.. urls.Select(url => new SearchResult
        {
            Title = url,
            Address = Link.Of(url),
            Snippet = string.Empty,
            Provider = name
        })]));

    private static ISearchProvider Empty(string name) => new StubProvider(name, _ => SearchAnswer.Success([]));

    private static ISearchProvider Faulting(string name)
        => new StubProvider(name, _ => throw new InvalidOperationException("provider is down"));

    private static ISearchProvider Refusing(string name)
        => new StubProvider(name, _ =>
            SearchAnswer.Failed(new SearchFailure(SearchFailureKind.Refused, "challenge page")));

    private sealed class StubProvider(string name, Func<string, SearchAnswer> answer, string? host = null)
        : ISearchProvider
    {
        public string Name => name;

        public string? Host => host;

        /// <summary>Whether this provider was asked at all, which is the whole point of a budget.</summary>
        public bool Asked { get; private set; }

        public Task<SearchAnswer> FindAsync(string query, CancellationToken ct = default)
        {
            Asked = true;

            return Task.FromResult(answer(query));
        }
    }

    /// <summary>A provider that answers late, and says when it is told to stop.</summary>
    private sealed class Slow(string name, TaskCompletionSource cancelled) : ISearchProvider
    {
        public string Name => name;

        public async Task<SearchAnswer> FindAsync(string query, CancellationToken ct = default)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }

            return SearchAnswer.Success([]);
        }
    }

    /// <summary>A provider that never answers, so what ends the attempt is the clock rather than the engine.</summary>
    private sealed class Waiting(string name) : ISearchProvider
    {
        public string Name => name;

        public async Task<SearchAnswer> FindAsync(string query, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);

            return SearchAnswer.Success([]);
        }
    }
}
