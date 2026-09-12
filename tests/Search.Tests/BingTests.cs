using Snail.Toolkit.Web.Search.Providers;

namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>
/// Covers the one thing Bing contributes over the other HTTP provider: its markup and its wrapper.
/// </summary>
public class BingTests
{
    /// <summary>
    /// Every result address is wrapped in a redirect with the real one base64url-encoded behind «a1».
    /// </summary>
    /// <remarks>
    /// Left wrapped, every result would be bing.com and deduplication across providers would collapse the
    /// whole answer to a single entry — the one failure that would look like an engine with nothing to say.
    /// </remarks>
    [Fact]
    public void Unwrapped_AWrappedAddress_IsTheRealOne()
        => Assert.Equal(
            "https://www.gazprom.ru/",
            Bing.Unwrapped("https://www.bing.com/ck/a?!&&p=8dc9&ptn=3&ver=2&hsh=4&u=a1aHR0cHM6Ly93d3cuZ2F6cHJvbS5ydS8&ntb=1"));

    [Fact]
    public void Unwrapped_AnAddressThatIsNotWrapped_IsLeftAlone()
        => Assert.Equal("https://example.com/x", Bing.Unwrapped("https://example.com/x"));

    /// <summary>A wrapper that stopped being base64 costs its own address, not the whole search.</summary>
    [Fact]
    public void Unwrapped_AWrapperThatStoppedBeingBase64_FallsBackToTheHref()
    {
        const string broken = "https://www.bing.com/ck/a?u=a1_____&ntb=1";

        Assert.Equal(broken, Bing.Unwrapped(broken));
    }

    [Fact]
    public void Parsed_AMarkupWithoutResults_ReportsNothing()
        => Assert.Empty(Bing.Parsed("""<html><body><ol id="b_results"></ol></body></html>"""));

    [Fact]
    public void Parsed_AResultBlock_ReadsTitleAddressAndSnippet()
    {
        const string html = """
            <ol id="b_results"><li class="b_algo"><h2><a href="https://www.bing.com/ck/a?u=a1aHR0cHM6Ly93d3cuZ2F6cHJvbS5ydS8&ntb=1">ПАО «Газпром»</a></h2>
            <div class="b_caption"><p class="b_lineclamp2">Крупнейшая газовая компания.</p></div></li></ol>
            """;

        var found = Bing.Parsed(html).Single();

        Assert.Equal("ПАО «Газпром»", found.Title);
        Assert.Equal("https://www.gazprom.ru/", found.Address.Canonical);
        Assert.Equal("Крупнейшая газовая компания.", found.Snippet);
        Assert.Equal(Bing.ProviderName, found.Provider);
    }
}
