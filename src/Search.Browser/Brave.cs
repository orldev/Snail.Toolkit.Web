using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;

namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// Asks Brave through a real browser.
/// </summary>
/// <remarks>
/// Measured against nine engines on 2026-09-12 from a datacentre address: this one answered where the others
/// would not. Google returned a captcha both by address and through its own form, Ecosia refused outright,
/// Yandex answered once and then demanded a captcha on the next two attempts, and Mojeek and Startpage named
/// nothing at all. Brave returned a hundred links across twenty-six sites and did it without wrapping them —
/// Bing and the DuckDuckGo endpoint both hide their results behind a redirect of their own, which has to be
/// unwrapped before a link is a link.
/// <para>
/// That is the whole reason this class is short: what differs per engine is the address a query becomes and
/// the markup it comes back as, and here neither needs any unwrapping.
/// </para>
/// </remarks>
public sealed class Brave(
    IPageReader pages,
    IOptions<BraveOptions> options,
    ILogger<Brave> logger) : BrowserSearch(pages, logger)
{
    /// <summary>The name this provider is registered and configured under.</summary>
    public const string ProviderName = "Brave";

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    protected override string Domain => options.Value.Domain;

    /// <inheritdoc />
    protected override Link AddressOf(string query)
        => Link.Of($"https://{Domain}/search?q={Uri.EscapeDataString(query)}");

    /// <inheritdoc />
    /// <remarks>
    /// Anchored on the data attributes the page marks its own results with, never on its class names. The
    /// classes carry a build hash — <c>svelte-jmfu5f</c>, <c>svelte-14r20fy</c> — which changes with every
    /// deployment, so a selector written against one would stop matching on an ordinary Tuesday and look
    /// exactly like a day when the engine found nothing. <c>data-type="web"</c> says what the block is.
    /// <para>
    /// The title is read from the <c>title</c> attribute before the element's text, because the text is
    /// clamped to one line in the markup while the attribute carries it whole.
    /// </para>
    /// </remarks>
    protected override IEnumerable<SearchResult> Parsed(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var blocks = document.DocumentNode.SelectNodes("//div[@data-type='web']");

        if (blocks is null)
            yield break;

        foreach (var block in blocks)
        {
            if (block.SelectSingleNode(".//a[@href]") is not { } anchor)
                continue;

            var href = anchor.GetAttributeValue("href", string.Empty).Trim();
            var title = TitleOf(block);

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

    private static string TitleOf(HtmlNode block)
    {
        var heading = block.SelectSingleNode(".//div[contains(@class,'search-snippet-title')]");

        if (heading is null)
            return string.Empty;

        var declared = HtmlEntity.DeEntitize(heading.GetAttributeValue("title", string.Empty)).Trim();

        return declared.Length > 0 ? declared : HtmlEntity.DeEntitize(heading.InnerText).Trim();
    }

    /// <summary>Reads the description shown under a result, when the engine had one to show.</summary>
    /// <remarks>
    /// A missing description is ordinary rather than a failure, and so is a placeholder: this engine prints a
    /// sentence saying it cannot describe the page when the site forbids it, which is not a summary of
    /// anything and is left out.
    /// </remarks>
    private static string SnippetOf(HtmlNode block)
    {
        var snippet = block.SelectSingleNode(".//div[contains(@class,'generic-snippet')]//div[contains(@class,'content')]");

        return snippet is null ? string.Empty : HtmlEntity.DeEntitize(snippet.InnerText).Trim();
    }
}
