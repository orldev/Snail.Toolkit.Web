using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;

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

    /// <summary>Reads the links off one results page.</summary>
    /// <remarks>
    /// Markup that cannot be followed must answer with nothing rather than throw: the caller then records an
    /// answered attempt that found nothing, and an engine whose layout changed shows up in the metrics as one
    /// that suddenly always answers empty. An exception per query would only look like an outage.
    /// <para>
    /// The results are handed over whole and the ceiling is applied by the caller of this, so an engine
    /// supplies a parser and nothing else. Until 2026-09-13 no ceiling was applied here at all:
    /// <see cref="SearchOptions.MaxResultsPerProvider"/> was read by the two engines that fetch over HTTP and
    /// by neither of the two that drive a browser, and Brave alone answered with a hundred links.
    /// </para>
    /// </remarks>
    protected abstract IEnumerable<SearchResult> Parsed(string html);

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

    /// <inheritdoc />
    public async Task<SearchAnswer> FindAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return SearchAnswer.Success([]);

        var outcome = await AnsweredAsync(query, ct);

        if (!outcome.IsSuccess)
            return SearchAnswer.Failed(Unread(outcome.Failure));

        var page = outcome.Value;
        var offered = await ResolvedAsync([.. Parsed(page.Html)], ct);

        IReadOnlyList<SearchResult> results =
        [
            .. offered
                .Where(result => IsOutbound(result.Address))
                .Take(MaxResults)
        ];

        if (results.Count == 0)
            logger.LogWarning(
                "{Provider} answered with {Length} characters of markup that yielded no results, which is what "
                + "a layout change looks like from here.",
                Name, page.Html.Length);

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
    private SearchFailure Unread(PageFailure failure)
    {
        if (failure is { Kind: FailureKind.Blocked } or { Kind: FailureKind.Navigation, StatusCode: 429 })
            return new SearchFailure(SearchFailureKind.Refused, $"{Name} could not be read. {failure}");

        return new SearchFailure(SearchFailureKind.Faulted, $"{Name} could not be read. {failure}");
    }
}
