using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;

namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// Asks Google through a real browser.
/// </summary>
/// <remarks>
/// Google is why this package exists — an HTTP client gets a consent wall or a <c>/sorry/</c> redirect — but it
/// is only one engine among several that behave this way, so everything that is not Google-specific lives in
/// <see cref="BrowserSearch"/>. What is left here is one address and one parser.
/// </remarks>
public sealed class Google(
    IPageReader pages,
    IOptions<GoogleOptions> options,
    IOptions<SearchOptions> search,
    ILogger<Google> logger) : BrowserSearch(pages, search, logger)
{
    /// <summary>The name this provider is registered and configured under.</summary>
    public const string ProviderName = "Google";

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    protected override string Domain => options.Value.Domain;

    /// <inheritdoc />
    protected override Link AddressOf(string query)
        => Link.Of($"https://{Domain}/search?q={Uri.EscapeDataString(query)}");

    /// <inheritdoc />
    /// <remarks>
    /// Anchored on the one shape that has survived every layout change: a link that contains a heading. Class
    /// names on this page are generated and change without notice, so matching them would break silently and
    /// look exactly like a day when nothing matched.
    /// </remarks>
    protected override IEnumerable<SearchResult> Parsed(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

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

    /// <summary>Unwraps the <c>/url?q=</c> redirect Google still serves on some paths.</summary>
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
