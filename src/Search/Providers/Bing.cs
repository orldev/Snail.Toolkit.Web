using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Search.Providers;

/// <summary>
/// Reads Bing's results page.
/// </summary>
/// <remarks>
/// An HTTP provider rather than a browser one because it answers a plain request: measured on 2026-09-12 from a
/// datacentre address it returned results where Google answered a captcha and Ecosia refused outright. That is
/// the whole reason to keep more than one engine registered — which of them answers a given client from a given
/// address is a fact that changes, and the aggregator is where it stops mattering.
/// <para>
/// Every result link is wrapped in a redirect whose target sits base64url-encoded in its <c>u</c> parameter
/// behind an <c>a1</c> marker. Left wrapped, every address would be bing.com and deduplication across providers
/// — the reason the address is a <see cref="Link"/> at all — would collapse the whole answer to one entry.
/// </para>
/// <para>
/// One thing is measured and unresolved: asked for a bare tax number rather than a brand name, this engine's
/// page mentions the number but named no outside site that the unwrapper could recover, while Brave, the
/// DuckDuckGo endpoint and Yandex all answered with the registries. Whether that is the engine or the reading is
/// not yet known, so an identifier query should not be trusted to this provider alone.
/// </para>
/// </remarks>
public sealed partial class Bing(IHttpClientFactory clients, IOptions<SearchOptions> options) : ISearchProvider
{
    /// <summary>The name this provider is registered and its HTTP client is keyed by.</summary>
    public const string ProviderName = "Bing";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> FindAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var wanted = options.Value.MaxResultsPerProvider;
        var client = clients.CreateClient(ProviderName);

        using var answered = await client.GetAsync(
            $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}&count={Math.Min(wanted, 30)}",
            ct);

        answered.EnsureSuccessStatusCode();

        var html = await answered.Content.ReadAsStringAsync(ct);
        var seen = new HashSet<Link>();

        return [.. Parsed(html).Where(result => seen.Add(result.Address)).Take(wanted)];
    }

    /// <summary>Reads the result blocks out of one page of markup.</summary>
    /// <remarks>
    /// Anchored on the heading inside a result block, which is the shape that survives layout changes; the class
    /// names beside it are generated. An empty list is the honest answer to a changed layout and to a query
    /// nothing matched alike — the caller records it as an answered attempt with nothing in it.
    /// </remarks>
    internal static IEnumerable<SearchResult> Parsed(string html)
    {
        var document = new HtmlDocument();

        document.LoadHtml(html);

        var blocks = document.DocumentNode.SelectNodes("//li[contains(@class,'b_algo')]");

        if (blocks is null)
            yield break;

        foreach (var block in blocks)
        {
            var anchor = block.SelectSingleNode(".//h2//a[@href]");

            if (anchor is null)
                continue;

            var title = HtmlEntity.DeEntitize(anchor.InnerText).Trim();
            var address = Unwrapped(HtmlEntity.DeEntitize(anchor.GetAttributeValue("href", string.Empty)).Trim());

            if (title.Length == 0 || address.Length == 0)
                continue;

            var snippet = block.SelectSingleNode(".//p");

            yield return new SearchResult
            {
                Title = title,
                Address = Link.Of(address),
                Snippet = snippet is null ? string.Empty : HtmlEntity.DeEntitize(snippet.InnerText).Trim(),
                Provider = ProviderName
            };
        }
    }

    /// <summary>Reads the real address out of the redirect Bing wraps every result in.</summary>
    /// <remarks>
    /// Falls back to the href it was given rather than throwing: a wrapper that stops being base64 is a changed
    /// layout, and a changed layout should cost the address it could not read, not the whole search.
    /// </remarks>
    internal static string Unwrapped(string href)
    {
        if (Wrapped().Match(href) is not { Success: true } wrapper)
            return href;

        var encoded = wrapper.Groups[1].Value.Replace('-', '+').Replace('_', '/');

        try
        {
            return Encoding.UTF8.GetString(
                Convert.FromBase64String(encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=')));
        }
        catch (FormatException)
        {
            return href;
        }
    }

    [GeneratedRegex(@"[?&]u=a1([A-Za-z0-9_\-]+)")]
    private static partial Regex Wrapped();
}
