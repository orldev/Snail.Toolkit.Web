using System.Globalization;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Search.Providers;

/// <summary>
/// Reads the DuckDuckGo HTML endpoint.
/// </summary>
/// <remarks>
/// The HTML endpoint is used rather than the JavaScript one because it is the same answer without a browser,
/// which is the whole reason this package can exist without Playwright.
/// Robots rules are not consulted here, and that is deliberate: this fetches the provider's own search
/// endpoint, published for exactly this, not the pages it names. Whoever reads those pages afterwards is the
/// one that has to obey them.
/// </remarks>
public sealed class DuckDuckGo(IHttpClientFactory clients, IOptions<SearchOptions> options) : ISearchProvider
{
    /// <summary>The name this provider is registered and configured under.</summary>
    public const string ProviderName = "DuckDuckGo";

    /// <inheritdoc />
    public string Name => ProviderName;

    private const int MaxRequests = 5;

    /// <inheritdoc />
    /// <remarks>
    /// Asks for a further page only while one is wanted and the page itself offers one, so the usual cost stays
    /// a single request. A caller that raised <c>MaxResultsPerProvider</c> past what one page holds used to be
    /// handed the page with nothing saying why the number was smaller than asked for.
    /// <para>
    /// The offset of the next page is read out of the page's own next-page control rather than counted from a
    /// page size written down here. A size constant would be a guess about someone else's markup, and a wrong
    /// one either skips results or asks for the same page until the ceiling stops it.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<SearchResult>> FindAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var wanted = options.Value.MaxResultsPerProvider;
        var client = clients.CreateClient(ProviderName);

        List<SearchResult> found = [];
        HashSet<Link> seen = [];
        int? offset = 0;

        for (var request = 0; request < MaxRequests && offset is { } start && found.Count < wanted; request++)
        {
            var html = await PageAsync(client, query, start, ct);
            var added = found.Count;

            foreach (var result in Parsed(html).Where(result => seen.Add(result.Address)).Take(wanted - added))
                found.Add(result);

            if (found.Count == added)
                break;

            offset = NextOffset(html);
        }

        return found;
    }

    /// <summary>Reads the offset the page's own next-page control asks for, when it offers one.</summary>
    /// <remarks>
    /// The largest offset on the page is the next one: the same markup carries a previous-page control at a
    /// smaller offset once past the first page.
    /// </remarks>
    internal static int? NextOffset(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        if (document.DocumentNode.SelectNodes("//input[@name='s'][@value]") is not { } controls)
            return null;

        var offsets = controls
            .Select(control => int.TryParse(
                control.GetAttributeValue("value", string.Empty),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var offset)
                ? offset
                : -1)
            .Where(offset => offset > 0)
            .ToArray();

        return offsets.Length == 0 ? null : offsets.Max();
    }

    private static async Task<string> PageAsync(HttpClient client, string query, int offset, CancellationToken ct)
    {
        var address = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
        var response = await client.GetAsync(offset == 0 ? address : $"{address}&s={offset}", ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Reads the result blocks out of one page of markup.</summary>
    /// <remarks>
    /// <c>SelectNodes</c> answers <see langword="null"/> when nothing matches, which is also what a challenge
    /// page or a changed layout looks like: an empty list is the honest answer to all three, and the caller
    /// records it as an answered attempt with nothing in it.
    /// </remarks>
    internal static IEnumerable<SearchResult> Parsed(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var blocks = document.DocumentNode.SelectNodes("//div[contains(@class,'web-result')]");

        if (blocks is null)
            yield break;

        foreach (var block in blocks)
        {
            var link = block.SelectSingleNode(".//a[contains(@class,'result__a')]");

            if (link is null)
                continue;

            var title = HtmlEntity.DeEntitize(link.InnerText.Trim());
            var href = link.GetAttributeValue("href", "").Trim();

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href))
                continue;

            var snippet = block.SelectSingleNode(".//a[contains(@class,'result__snippet')]");

            yield return new SearchResult
            {
                Title = title,
                Address = Link.Of(Unwrapped(href)),
                Snippet = snippet is null ? string.Empty : HtmlEntity.DeEntitize(snippet.InnerText.Trim()),
                Provider = ProviderName
            };
        }
    }

    /// <summary>Unwraps the redirect DuckDuckGo wraps its outbound links in.</summary>
    /// <remarks>
    /// Falls back to the href it was given rather than throwing. Parsing ran outside any guard before, so a
    /// single malformed link discarded every result already parsed from the page.
    /// </remarks>
    private static string Unwrapped(string href)
    {
        if (!href.Contains("uddg=", StringComparison.Ordinal))
            return href;

        if (!Uri.TryCreate(new Uri("https://duckduckgo.com"), href, out var absolute))
            return href;

        var wrapped = absolute.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(pair => pair.StartsWith("uddg=", StringComparison.Ordinal));

        return wrapped is null ? href : Uri.UnescapeDataString(wrapped["uddg=".Length..]);
    }
}
