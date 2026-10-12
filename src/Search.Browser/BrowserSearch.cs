using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// A search engine that only answers a real browser.
/// </summary>
/// <remarks>
/// Google is not alone in this: Bing, Yandex, Brave and Baidu all meet a plain HTTP client with a consent wall
/// or a challenge, and every one of them then needs the same handling — read the page with a browser, tell a
/// refusal from a page that merely could not be read, drop the links that are the page's own furniture, and
/// say so when a page that loaded fine parsed into nothing. Only two things differ per engine: the address a
/// query becomes, and the markup it comes back as. Those are what a subclass supplies.
/// <para>
/// How the page is obtained is left to the engine, because the two ways differ in more than an address. An
/// engine that answers a link is read through <see cref="IPageReader"/>; one that answers only a form filled
/// in by hand is walked through by the page agent. Measured on 2026-09-13, that is not a preference: asked at
/// its results address Google answers <c>/sorry/</c> and asked through its own form it returns results, with a
/// passing control on either side of the refusal. Either way the engine inherits the per-host gate, the
/// coherent identity, the stealth script, challenge detection and the retry policy instead of restating them.
/// </para>
/// An engine is a type of its own rather than a configured instance because that is what lets it be registered
/// with <c>AddSearchProvider&lt;T&gt;()</c> and stay idempotent: a factory registration could not be deduplicated
/// and a second call would ask the same engine twice per query.
/// </remarks>
public abstract class BrowserSearch(IOptions<SearchOptions> search, ILogger logger)
    : ISearchProvider
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <summary>Gets the host queries are sent to, without a scheme.</summary>
    protected abstract string Domain { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The same host the engine is read at, said once more where the search can see it: a caller weighing what
    /// a query costs asks the gate about this, and an engine driven through a browser is the expensive kind.
    /// </remarks>
    public string Host => Domain;

    /// <summary>Gets the most results one query may answer with.</summary>
    /// <remarks>
    /// Read here rather than by each engine so that an engine which pays per link — one that has to ask where
    /// each of them leads — can stop at the same number the caller would have cut the list to anyway.
    /// </remarks>
    protected int MaxResults => search.Value.MaxResultsPerProvider;

    /// <summary>Gets the subdomain prefixes that belong to the engine itself rather than to its results.</summary>
    /// <remarks>
    /// Sign-in, policies, help and the cache all appear as ordinary links on a results page. A result that
    /// genuinely lives on the engine's own company domain — a developer document, say — is kept: excluding
    /// everything that looks like the engine would drop real answers to real queries.
    /// </remarks>
    protected virtual IReadOnlyList<string> ServiceHosts { get; } =
        ["accounts.", "policies.", "support.", "webcache."];

    /// <summary>Asks the engine one query and hands back the page it answered with.</summary>
    protected abstract Task<PageOutcome> AnsweredAsync(string query, CancellationToken ct);

    /// <summary>Reads the links off one results page, already parsed.</summary>
    /// <remarks>
    /// The document rather than its markup since 2026-09-15, and the reason is the ceiling on nesting depth:
    /// an engine handed a string parses it itself, and four engines parsing their own were four ways past the
    /// one door the parser is supposed to have. Parsing here also means an engine cannot forget it.
    /// <para>
    /// Markup that cannot be followed must answer with nothing rather than throw: the caller then records an
    /// answered attempt that found nothing, and an engine whose layout changed shows up in the metrics as one
    /// that suddenly always answers empty. An exception per query would only look like an outage.
    /// </para>
    /// <para>
    /// The results are handed over whole and the ceiling is applied by the caller of this, so an engine
    /// supplies a parser and nothing else. Until 2026-09-13 no ceiling was applied here at all:
    /// <see cref="SearchOptions.MaxResultsPerProvider"/> was read by the two engines that fetch over HTTP and
    /// by neither of the two that drive a browser, and Brave alone answered with a hundred links.
    /// </para>
    /// </remarks>
    protected abstract IEnumerable<SearchResult> Parsed(HtmlDocument document);

    /// <summary>Turns the addresses a results page carries into the addresses they lead to.</summary>
    /// <remarks>
    /// An engine whose markup states the address it means needs nothing here, and the default does nothing.
    /// Google is the reason this exists: measured on 2026-09-14, every result on its page is a relative
    /// <c>/goto</c> link whose query is an encrypted blob, and the address behind it is known only by asking
    /// the engine where the link leads. Asking costs one request per link, which is why the engine that pays
    /// it also caps the list here rather than handing over everything its parser found.
    /// <para>
    /// This runs before <see cref="IsOutbound"/> and not after, because a wrapper carries no host at all: the
    /// filter would read every one of them as belonging to the engine and drop the whole page. Until
    /// 2026-09-14 it did exactly that, and Google answered a megabyte of markup with no results.
    /// </para>
    /// </remarks>
    protected virtual Task<IReadOnlyList<SearchResult>> ResolvedAsync(
        IReadOnlyList<SearchResult> results,
        CancellationToken ct)
        => Task.FromResult(results);

    /// <summary>Whether the page the engine answered with is an answer to the query that was asked.</summary>
    /// <remarks>
    /// The default says yes, because most engines either answer or refuse and there is no third thing to
    /// name. Bing is the third thing, measured on 2026-09-15: asked in Russian from a datacentre address it
    /// sometimes serves a page of ten confident results about something else entirely — bible sites for
    /// «Rusnano» (in Russian), Italian pages about e-cigarettes for «QIWI Bank licence revoked» (in Russian) — and a parser cannot tell
    /// that from an answer, because structurally it IS one.
    /// <para>
    /// An engine that can be served that page overrides this and says so, and the search then records a
    /// refusal rather than results. Left here rather than pushed into every engine because the caller's
    /// question — «did I get an answer to what I asked» — is the same for all of them; only the evidence
    /// differs, and only an engine that has been measured should claim to have any.
    /// </para>
    /// </remarks>
    protected virtual bool Answers(string query, IReadOnlyList<SearchResult> results)
        => Relevance.Answers(query, results);

    /// <inheritdoc />
    /// <remarks>
    /// Bounded by <see cref="SearchOptions.Timeout"/>, the same setting that bounds an engine asked over HTTP.
    /// Until 2026-09-20 it bounded only those: it was wired into the HTTP client's pipeline and nowhere else,
    /// so a browser engine lasted as long as its navigation timeouts times its retries — a consumer measured
    /// 136 to 392 seconds against a one-minute setting. Running out of it is a fault, as it is over HTTP,
    /// and a cancellation by the caller stays a cancellation.
    /// </remarks>
    public async Task<SearchAnswer> FindAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return SearchAnswer.Success([]);

        var limit = search.Value.Timeout;

        using var limited = CancellationTokenSource.CreateLinkedTokenSource(ct);

        limited.CancelAfter(limit);

        try
        {
            return await ReadAsync(query, limited.Token);
        }
        catch (OperationCanceledException) when (limited.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return SearchAnswer.Failed(new SearchFailure(
                SearchFailureKind.Faulted,
                $"{Name} did not answer within {limit}."));
        }
    }

    private async Task<SearchAnswer> ReadAsync(string query, CancellationToken ct)
    {
        var outcome = await AnsweredAsync(query, ct);

        if (!outcome.IsSuccess)
            return SearchAnswer.Failed(Unread(outcome.Failure));

        var page = outcome.Value;

        HtmlDocument document;

        try
        {
            document = Markup.Read(page.Html);
        }
        catch (MarkupTooDeepException tooDeep)
        {
            return SearchAnswer.Failed(SearchFailure.Unreadable(Name, tooDeep));
        }

        var offered = await ResolvedAsync([.. Parsed(document)], ct);

        IReadOnlyList<SearchResult> results =
        [
            .. offered
                .Where(result => IsOutbound(result.Address))
                .Take(MaxResults)
        ];

        if (results.Count == 0)
            Log.NoResults(logger, Name, page.Html.Length);

        if (results.Count > 0 && !Answers(query, results))
            return SearchAnswer.Failed(SearchFailure.Unrelated(Name, results.Count));

        return SearchAnswer.Success(results);
    }

    /// <summary>Answers whether a link leads off the results page rather than around it.</summary>
    protected virtual bool IsOutbound(Link address)
    {
        if (address.Address is not { } uri)
            return false;

        if (string.Equals(uri.Host, Domain, StringComparison.OrdinalIgnoreCase))
            return false;

        return !ServiceHosts.Any(host => uri.Host.StartsWith(host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Tells a page the engine refused to show from one that merely could not be read.</summary>
    /// <remarks>
    /// Only two failures are the engine's answer to being asked too often: a challenge or refusal page, and a
    /// 429 that says so in the status. A timeout, an unreachable browser, a proxy that would not connect or a
    /// DNS failure say nothing about the engine's patience, and the search — which rests an engine over a
    /// refusal — would otherwise leave a working engine out over a hiccup on this side.
    /// </remarks>
    /// <summary>Reads a page that could not be read as the engine refusing, or as a failure on this side.</summary>
    /// <remarks>
    /// Which statuses mean "not you, not now" is <see cref="PageFailure.IsRefusal"/>'s to say, and it was this
    /// method's own list until 2026-09-20 — a shorter one. It held a wall and a <c>429</c>, so a browser engine
    /// answering <c>403</c>, the shape a consent wall and a blocked address both arrive in, was recorded as a
    /// fault: never rested, asked again at the next query. The HTTP side had read the same status as a refusal
    /// since 2026-09-14, and the two roads disagreeing about one engine is the disagreement worth removing.
    /// </remarks>
    private SearchFailure Unread(PageFailure failure)
        => new(
            failure.IsRefusal ? SearchFailureKind.Refused : SearchFailureKind.Faulted,
            $"{Name} could not be read. {failure}");
}
