using System.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// Asks Bing through a real browser.
/// </summary>
/// <remarks>
/// The same engine this library already asks over plain HTTP, read the way that works. Measured on
/// 2026-09-15 from a datacentre address, five queries each, the queries written in Russian and quoted here in
/// English: over HTTP the engine answered «"Rusnano"» with bible sites, «Rusnano» with a hotel chain, «Rosnano»
/// in Latin letters with Zurich tourism and «QIWI Bank licence revoked» with car parts — ten confident results
/// per query, about something else entirely. Through a browser the same address answered «Rusnano» with the
/// Rusnano Group, the Wikipedia article and the company's own press centre, three times out of three, and
/// «QIWI Bank licence revoked» with «Licence for banking operations revoked from…» and «What is known about the
/// revocation of QIWI Bank's licence».
/// <para>
/// THE FIRST READ IN A FRESH CONTEXT IS THE UNRELIABLE ONE. Of three queries measured three times each, two
/// failed on their first attempt — one with an empty page, one with a navigation that never settled — and
/// answered properly on the second and third. That is why the page is taken whole rather than as the
/// document alone: <see cref="Appetite.DocumentOnly"/> was measured too, and it returned the stale or wrong
/// page where the full load returned the right one.
/// </para>
/// <para>
/// Every result link is wrapped in the engine's own redirect — <c>/ck/a?…&amp;u=a1&lt;base64url&gt;</c> — and
/// unwrapping it costs nothing, unlike Google's <c>/goto</c>, which has to be asked where it leads. The
/// address is inside the wrapper, base64url after the two-character marker.
/// </para>
/// </remarks>
public sealed class Bing(
    [FromKeyedServices(EngineReading.Key)] IPageReader pages,
    IOptions<BingOptions> options,
    IOptions<SearchOptions> search,
    ILogger<Bing> logger) : BrowserSearch(search, logger)
{
    /// <summary>The name this provider is registered and configured under.</summary>
    public const string ProviderName = "Bing";

    /// <summary>What the engine puts in front of the address it wraps.</summary>
    private const string Marker = "a1";

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    protected override string Domain => options.Value.Domain;

    /// <inheritdoc />
    /// <remarks>
    /// The whole page, not the document alone. Measured on 2026-09-15 against the same three queries: the
    /// document alone answered «"QIWI Bank"» (the bank's name half in Cyrillic) with the same four links the plain HTTP client gets, while the
    /// full load answered with the article about the licence being revoked — the engine finishes the page
    /// with script, and the half we would save is the half with the results in it.
    /// </remarks>
    protected override Task<PageOutcome> AnsweredAsync(string query, CancellationToken ct)
        => pages.ReadAsync(AddressOf(query), Appetite.Everything, ct);

    private Link AddressOf(string query)
        => Link.Of($"https://{Domain}/search?q={Uri.EscapeDataString(query)}");

    /// <inheritdoc />
    /// <remarks>
    /// Anchored on <c>li.b_algo</c>, which is what the engine has marked an ordinary web result with for
    /// years, and on the heading inside it. Blocks that carry no heading are the page's own furniture —
    /// deep-link tables, rich cards, related searches — and are skipped rather than guessed at.
    /// <para>
    /// De-entitized, and the recorded page is why: the engine writes its wrapper with «&amp;amp;» between the
    /// parameters, and an address read raw carries «amp;u» where «u» should be — so every link would stay wrapped and
    /// be dropped as the engine's own.
    /// </para>
    /// </remarks>
    protected override IEnumerable<SearchResult> Parsed(HtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var blocks = document.DocumentNode.SelectNodes("//li[contains(concat(' ', normalize-space(@class), ' '), ' b_algo ')]");

        if (blocks is null)
            yield break;

        foreach (var block in blocks)
        {
            if (block.SelectSingleNode(".//h2//a[@href]") is not { } anchor)
                continue;

            var href = HtmlEntity.DeEntitize(anchor.GetAttributeValue("href", string.Empty)).Trim();
            var title = HtmlEntity.DeEntitize(anchor.InnerText).Trim();

            if (href.Length == 0 || title.Length == 0)
                continue;

            yield return new SearchResult
            {
                Title = title,
                Address = Link.Of(href),
                Snippet = SnippetOf(block),
                Provider = ProviderName
            };
        }
    }

    /// <summary>The description printed under a result, when the engine printed one.</summary>
    private static string SnippetOf(HtmlNode block)
    {
        var caption = block.SelectSingleNode(".//div[contains(@class,'b_caption')]//p")
                      ?? block.SelectSingleNode(".//p");

        return caption is null ? string.Empty : HtmlEntity.DeEntitize(caption.InnerText).Trim();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Free, and that is the whole difference from Google's wrapper: the address is carried inside the
    /// redirect rather than kept by the engine, so unwrapping is a decode instead of a request per link. A
    /// wrapper that does not decode is left as it is — it still leads where it leads, and dropping it would
    /// lose a result over a format change.
    /// </remarks>
    protected override Task<IReadOnlyList<SearchResult>> ResolvedAsync(
        IReadOnlyList<SearchResult> results,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(results);

        IReadOnlyList<SearchResult> unwrapped =
        [
            .. results.Select(result => Unwrapped(result.Address) is { } address
                ? result with { Address = address }
                : result)
        ];

        return Task.FromResult(unwrapped);
    }

    /// <summary>The address a redirect carries, or nothing when the link is not one.</summary>
    private static Link? Unwrapped(Link address)
    {
        if (address.Address is not { } uri || !uri.AbsolutePath.StartsWith("/ck/a", StringComparison.OrdinalIgnoreCase))
            return null;

        var carried = System.Web.HttpUtility.ParseQueryString(uri.Query)["u"];

        if (carried is not { Length: > 2 } || !carried.StartsWith(Marker, StringComparison.Ordinal))
            return null;

        try
        {
            var encoded = carried[Marker.Length..].Replace('-', '+').Replace('_', '/');
            var padded = encoded.PadRight(encoded.Length + ((4 - (encoded.Length % 4)) % 4), '=');
            var said = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

            return Uri.IsWellFormedUriString(said, UriKind.Absolute) ? Link.Of(said) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
