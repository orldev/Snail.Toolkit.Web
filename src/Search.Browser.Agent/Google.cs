using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Scraping.Agent;

namespace Snail.Toolkit.Web.Search.Browser.Agent;

/// <summary>
/// Asks Google through a real browser.
/// </summary>
/// <remarks>
/// Google is why this package exists, and it is asked the way a person asks: the home page, the query typed
/// into the field a key at a time, then the return key. Measured on 2026-09-13 from one address, a fresh
/// context per arm and twelve minutes between them: typed through the form it returned seven results, asked
/// at its own results address it answered <c>/sorry/</c>, and typed again it returned seven results. The path
/// in is therefore not decoration, and it cannot be expressed as a link — which is why this engine lives
/// beside <see cref="BrowserSearch"/> rather than inside the package that only knows addresses.
/// <para>
/// The consent wall was not met from that address and so is not in the errand. A deployment that meets it
/// adds a step for it; a step for an element that is usually absent would fail every ordinary run instead.
/// </para>
/// Everything that is not Google-specific — the outbound filter, the ceiling, telling a refusal from an
/// unreadable page — stays in <see cref="BrowserSearch"/>. What is left here is one errand, one parser, and
/// the request that turns a wrapped link into the address it wraps.
/// </remarks>
public sealed class Google(
    [FromKeyedServices(EngineReading.Key)] IPageAgent agent,
    IHttpClientFactory clients,
    IOptions<GoogleOptions> options,
    IOptions<SearchOptions> search,
    IOptions<PolitenessOptions> pacing,
    ILogger<Google> logger) : BrowserSearch(search, logger)
{
    private const string Field = "textarea[name=\"q\"]";

    /// <summary>The name this provider is registered and configured under.</summary>
    public const string ProviderName = "Google";

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    protected override string Domain => options.Value.Domain;

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="StepKind.Press"/> carries no key because it defaults to the return key, and the return key
    /// is the only thing that submits this form: measured across five engines on 2026-09-12, two offer no
    /// submitting element at all and a third ignores a click on the one it has.
    /// </remarks>
    /// <inheritdoc />
    /// <remarks>
    /// A REFUSAL NAMES THE GAP IN FORCE, and that line is the whole of what this package can honestly say about
    /// how often this engine may be asked. The two minutes it writes by default is the shortest interval that
    /// passed in one measurement of four queries on clean contexts (2026-09-13); the rate the engine SUSTAINS
    /// has never been measured, and it could not be measured here in any case — tolerance is per address, so
    /// the only measurement that counts is the deployment's own. Naming the number at the moment it proved too
    /// small is what turns a constant somebody wrote into something an operator can correct.
    /// </remarks>
    protected override async Task<PageOutcome> AnsweredAsync(string query, CancellationToken ct)
    {
        var outcome = await agent.PerformAsync(
            new Errand(Link.Of($"https://{Domain}/"),
            [
                new Step(StepKind.Type, Spot.Css(Field)) { Value = query },
                new Step(StepKind.Press, Spot.Css(Field))
            ]),
            ct);

        if (!outcome.IsSuccess && outcome.Failure.IsRefusal)
            Log.Refused(logger, ProviderName, Domain, pacing.Value.GapFor(Domain) ?? pacing.Value.MinDelayPerHost);

        return outcome;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Anchored on the one shape that has survived every layout change: a link that contains a heading. Class
    /// names on this page are generated and change without notice, so matching them would break silently and
    /// look exactly like a day when nothing matched.
    /// </remarks>
    protected override IEnumerable<SearchResult> Parsed(HtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var anchors = document.DocumentNode.SelectNodes("//a[@href][.//h3]");

        if (anchors is null)
            yield break;

        foreach (var anchor in anchors)
        {
            if (anchor.SelectSingleNode(".//h3") is not { } heading)
                continue;

            var title = HtmlEntity.DeEntitize(heading.InnerText).Trim();

            if (title.Length == 0)
                continue;

            yield return new SearchResult
            {
                Title = title,
                Address = Link.Of(Unwrapped(anchor.GetAttributeValue("href", string.Empty).Trim())),
                Snippet = SnippetOf(anchor),
                Provider = ProviderName
            };
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every result Google shows is a wrapper, so this is where a page of results becomes a page of addresses.
    /// The list is capped here rather than by the caller because each link costs a request: measured on
    /// 2026-09-14 the page carried six results and six wrappers, and resolving links the caller would discard
    /// would be paid for in requests to an engine that answers frequency with a challenge.
    /// <para>
    /// A result whose address is already absolute is left alone and costs nothing — the older <c>/url?q=</c>
    /// form unwraps in the parser without asking anyone.
    /// </para>
    /// </remarks>
    protected override async Task<IReadOnlyList<SearchResult>> ResolvedAsync(
        IReadOnlyList<SearchResult> results,
        CancellationToken ct)
    {
        var client = clients.CreateClient(ProviderName);
        var followed = new List<SearchResult>(results.Count);
        var asked = false;

        foreach (var result in results.Take(MaxResults))
        {
            if (result.Address.IsAbsolute)
            {
                followed.Add(result);
                continue;
            }

            if (asked)
                await Task.Delay(options.Value.FollowDelay, ct);

            var destination = await DestinationAsync(client, result.Address, ct);

            followed.Add(destination is null ? result : result with { Address = destination });
            asked = true;
        }

        return followed;
    }

    /// <summary>Asks the engine where one wrapped link leads, without going there.</summary>
    /// <remarks>
    /// The redirect is deliberately not followed and the body never read: the answer is the <c>Location</c>
    /// header alone, and following it would fetch every result page nobody has asked for yet. Measured on
    /// 2026-09-14, the wrapper answers <c>302</c> with the address to a client carrying no cookies, no referer
    /// and no session, which is why this is a plain request rather than something the browser has to make.
    /// <para>
    /// A wrapper that answers anything else leaves the result exactly as the page gave it, and the relative
    /// address it still carries is dropped by the outbound filter. A link nobody can follow is not a result,
    /// and a page where none of them can be followed is reported by the empty-answer warning for what it is.
    /// </para>
    /// </remarks>
    private async Task<Link?> DestinationAsync(HttpClient client, Link wrapper, CancellationToken ct)
    {
        if (!Uri.TryCreate(new Uri($"https://{Domain}/"), wrapper.Text, out var target))
            return null;

        try
        {
            using var reply = await client.GetAsync(target, ct);

            return reply.Headers.Location is { } destination ? Link.Of(destination.ToString()) : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Unwraps the <c>/url?q=</c> redirect Google served on some paths.</summary>
    /// <remarks>
    /// The claim is dated on purpose. This said Google "still" served that form, and the sentence became false
    /// without a single edit to the code: as of 2026-09-14 the engine wraps results in <c>/goto</c> instead,
    /// whose query is encrypted and cannot be unwrapped here at all — <see cref="ResolvedAsync"/> has to ask.
    /// The older form still appears on some paths and costs nothing to read, so it stays.
    /// </remarks>
    private static string Unwrapped(string href)
    {
        if (!href.StartsWith("/url?", StringComparison.Ordinal))
            return href;

        var wrapped = href
            .Split('?', 2)[1]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(pair => pair.StartsWith("q=", StringComparison.Ordinal));

        return wrapped is null ? href : Uri.UnescapeDataString(wrapped["q=".Length..]);
    }

    /// <summary>Reads the description Google showed under a link, when it showed one.</summary>
    /// <remarks>
    /// A missing snippet is ordinary rather than a failure: image, video and knowledge blocks carry a link and a
    /// heading with no prose at all.
    /// </remarks>
    private static string SnippetOf(HtmlNode anchor)
    {
        var block = anchor.SelectSingleNode("ancestor::div[@data-hveid][1]");
        var snippet = block?.SelectSingleNode(".//div[@data-sncf]");

        return snippet is null ? string.Empty : HtmlEntity.DeEntitize(snippet.InnerText).Trim();
    }
}
