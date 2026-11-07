using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// Asks DuckDuckGo through a real browser, at the site a person uses.
/// </summary>
/// <remarks>
/// Beside the provider that reads the HTML endpoint over plain HTTP, not instead of it: that one needs nothing
/// running and answers from most addresses. This one is for the addresses it does not answer. Measured on
/// 2026-11-07 from one such address, one query per arm with a plain request first and last as the control:
/// <list type="bullet">
/// <item>a plain request to the HTML endpoint — <c>202</c> and the challenge, both times;</item>
/// <item>Firefox at the HTML endpoint — the same challenge;</item>
/// <item>stock headless Chromium at the site — sent to <c>/static-pages/home-error/418.html</c>, twice;</item>
/// <item>Firefox at the site — ten results, four times out of four, two of them a query in Russian.</item>
/// </list>
/// From another address the plain request answered the same query with results, so the refusal belongs to the
/// address and not to the query.
/// <para>
/// So the site, read whole: its results are drawn by script after the document has loaded — none was in the
/// document at <c>DOMContentLoaded</c>, and the first appeared 340 to 870 ms after it — which is why the reader
/// is asked to wait for the first result rather than take the page at the document.
/// </para>
/// <para>
/// Its result links are the addresses themselves. The HTML endpoint wraps every one in a redirect of its own;
/// the site does not, and there is nothing to unwrap.
/// </para>
/// <para>
/// It shares its type name with <see cref="Providers.DuckDuckGo"/>, as <see cref="Bing"/> and <see cref="Brave"/>
/// are named after their engines; the namespace says which reading it is. Code that imports both namespaces names
/// one of them in full or through an alias — <c>using DuckDuckGoBrowser = Snail.Toolkit.Web.Search.Browser.DuckDuckGo;</c>.
/// Only <see cref="ProviderName"/> has to differ, and does.
/// </para>
/// </remarks>
public sealed class DuckDuckGo(
    [FromKeyedServices(EngineReading.Key)] IPageReader pages,
    IOptions<DuckDuckGoOptions> options,
    IOptions<SearchOptions> search,
    ILogger<DuckDuckGo> logger) : BrowserSearch(search, logger)
{
    /// <summary>The name this provider is registered and configured under.</summary>
    /// <remarks>
    /// Not <c>DuckDuckGo</c>, which names the provider over HTTP, though the type names are the same: the search
    /// keeps an engine's rest and its settings under this string, and the two readings are refused and answered
    /// separately — a challenge to the HTTP endpoint must not rest the browser, which is there for exactly that.
    /// </remarks>
    public const string ProviderName = "DuckDuckGoBrowser";

    /// <summary>What the site marks each of its web results with.</summary>
    private const string Result = "article[data-testid='result']";

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    protected override string Domain => options.Value.Domain;

    /// <inheritdoc />
    /// <remarks>
    /// The whole page, because the results arrive by script and the document alone would refuse the request
    /// that carries them.
    /// </remarks>
    protected override Task<PageOutcome> AnsweredAsync(string query, CancellationToken ct)
        => pages.ReadWhenReadyAsync(AddressOf(query), Appetite.Everything, Result, ct);

    private Link AddressOf(string query)
        => Link.Of($"https://{Domain}/?q={Uri.EscapeDataString(query)}&ia=web");

    /// <inheritdoc />
    /// <remarks>
    /// Anchored on the <c>data-testid</c> attributes the site marks its own results with, never on its class
    /// names, which are build hashes — <c>yQDlj3B5DI5YO8c8Ulio</c>, <c>eVNpHGjtxRBq_gLOfGDr</c> — that change with
    /// a deployment. The title is the result's own heading link; the sitelinks listed under some results are
    /// links of the same site and are not read as results of their own.
    /// </remarks>
    protected override IEnumerable<SearchResult> Parsed(HtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var blocks = document.DocumentNode.SelectNodes("//article[@data-testid='result']");

        if (blocks is null)
            yield break;

        foreach (var block in blocks)
        {
            if (block.SelectSingleNode(".//a[@data-testid='result-title-a'][@href]") is not { } anchor)
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

    /// <summary>The description printed under a result, when the site printed one.</summary>
    private static string SnippetOf(HtmlNode block)
    {
        var snippet = block.SelectSingleNode(".//div[@data-result='snippet']");

        return snippet is null ? string.Empty : HtmlEntity.DeEntitize(snippet.InnerText).Trim();
    }
}
