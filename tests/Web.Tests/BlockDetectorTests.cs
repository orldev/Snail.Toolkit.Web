using Snail.Toolkit.Web.Detection;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the difference between a site with nothing to say and one that refused to talk.
/// </summary>
public class BlockDetectorTests
{
    private readonly BlockDetector _detector = new();

    [Fact]
    public void Inspect_CloudflareChallenge_ReportsBlocked()
    {
        var verdict = _detector.Inspect(
            "<html><body><div id=\"cf-browser-verification\"></div></body></html>",
            "https://example.com/catalog");

        Assert.True(verdict.IsBlocked);
        Assert.Contains("cf-browser-verification", verdict.Signal!, StringComparison.Ordinal);
    }

    /// <summary>A redirect carries no markup marker, which is how these refusals used to pass as success.</summary>
    [Fact]
    public void Inspect_RedirectToSorryPage_ReportsBlocked()
    {
        var verdict = _detector.Inspect("<html><body>Ok</body></html>", "https://www.google.com/sorry/index?continue=x");

        Assert.True(verdict.IsBlocked);
        Assert.Equal("url:/sorry/", verdict.Signal);
    }

    [Fact]
    public void Inspect_RedirectToConsentWall_ReportsBlocked()
    {
        var verdict = _detector.Inspect("<html><body>Ok</body></html>", "https://consent.google.com/m?continue=x");

        Assert.True(verdict.IsBlocked);
    }

    /// <summary>
    /// Guards the deliberate omission of the bare words "captcha" and "blocked" from the markup signals.
    /// </summary>
    /// <remarks>
    /// A false block costs a proxy and a retry and hides a page that was fetched successfully, so an article
    /// that merely discusses captchas must not be mistaken for a challenge.
    /// </remarks>
    [Fact]
    public void Inspect_ArticleDiscussingCaptchas_ReportsClear()
    {
        var verdict = _detector.Inspect(
            "<html><body><h1>Why captcha puzzles are getting harder</h1><p>Access was blocked for bots.</p></body></html>",
            "https://news.example/article/1234");

        Assert.False(verdict.IsBlocked);
        Assert.Null(verdict.Signal);
    }

    /// <summary>The address is held to the same rule as the markup: no word an ordinary page may carry.</summary>
    /// <remarks>
    /// The guard above chose an address without the word, and so never noticed that the bare word stood in the
    /// URL list until 2026-09-13. An article about captchas was read as a refusal, and a refusal is transient,
    /// so the reader spent three identities proving it again before giving up.
    /// </remarks>
    [Theory]
    [InlineData("https://news.example/blog/how-captcha-works")]
    [InlineData("https://news.example/tags/captcha")]
    [InlineData("https://news.example/search?q=captcha")]
    public void Inspect_AddressNamingCaptchaInOrdinaryProse_ReportsClear(string address)
    {
        var verdict = _detector.Inspect("<html><body><p>A page about challenges.</p></body></html>", address);

        Assert.False(verdict.IsBlocked);
    }

    /// <summary>
    /// The hidden field an invisible reCAPTCHA injects is not a challenge.
    /// </summary>
    /// <remarks>
    /// Measured on a live registry on 2026-09-12: its rendered page carries <c>g-recaptcha</c> three times and
    /// all three are the <c>g-recaptcha-response</c> field of an invisible v3 token. Reading that as a block
    /// refused a page that had answered in full, and refused it on exactly the pages a caller most needs — a
    /// form guarded by an invisible token is one whose service answers no one but its own page. The markup the
    /// server sends carries none of it, which is why only a rendered document shows the defect.
    /// </remarks>
    [Fact]
    public void Inspect_InvisibleRecaptchaTokenField_ReportsClear()
    {
        var verdict = _detector.Inspect(
            """<html><body><form><textarea id="g-recaptcha-response-100000" name="g-recaptcha-response" class="g-recaptcha-response"></textarea></form></body></html>""",
            "https://www.reestr-zalogov.ru/search/index");

        Assert.False(verdict.IsBlocked);
        Assert.Null(verdict.Signal);
    }

    /// <summary>The visible widget is a challenge and still matches.</summary>
    [Fact]
    public void Inspect_RecaptchaChallengeWidget_ReportsBlocked()
    {
        var verdict = _detector.Inspect(
            """<html><body><div class="g-recaptcha" data-sitekey="6LdKJhMa"></div></body></html>""",
            "https://example.com/login");

        Assert.True(verdict.IsBlocked);
        Assert.Equal("markup:g-recaptcha", verdict.Signal);
    }

    /// <summary>A site naming the captcha it uses in its own settings is not showing one.</summary>
    /// <remarks>
    /// Measured on 2026-10-04 through the MCP server built on this toolkit: every page of Wikipedia carries
    /// the line below in the settings MediaWiki inlines into the document, and the bare word <c>hcaptcha</c>
    /// in the signal list called an article that had loaded in full — status 200, 962 074 bytes — a wall.
    /// The first two articles asked for both came back <c>Blocked: markup:hcaptcha</c>.
    /// </remarks>
    [Fact]
    public void Inspect_APageNamingHcaptchaInItsOwnSettings_ReportsClear()
    {
        var verdict = _detector.Inspect(
            """
            <html><head><script>RLCONF={"wgNamespaceIds":{"special":-1},"wgConfirmEditCaptchaNeededForGenericEdit":"hcaptcha","wgConfirmEditForceShowCaptcha":false,"wgConfirmEditHCaptchaSiteKey":"5d0c670e"};</script></head>
            <body><h1>PostgreSQL</h1><p>PostgreSQL is a free and open-source relational database management system.</p></body></html>
            """,
            "https://en.wikipedia.org/wiki/PostgreSQL");

        Assert.False(verdict.IsBlocked);
        Assert.Null(verdict.Signal);
    }

    /// <summary>The widget a visitor has to solve is still a challenge.</summary>
    [Fact]
    public void Inspect_HcaptchaChallengeWidget_ReportsBlocked()
    {
        var verdict = _detector.Inspect(
            """<html><body><div class="h-captcha" data-sitekey="10000000-ffff-ffff-ffff-000000000001"></div></body></html>""",
            "https://example.com/login");

        Assert.True(verdict.IsBlocked);
        Assert.Equal("markup:h-captcha", verdict.Signal);
    }

    /// <summary>The hidden field hCaptcha injects is named after reCAPTCHA's, and is no more a challenge than that one.</summary>
    [Fact]
    public void Inspect_InvisibleHcaptchaTokenField_ReportsClear()
    {
        var verdict = _detector.Inspect(
            """<html><body><form><textarea name="h-captcha-response" style="display:none"></textarea></form></body></html>""",
            "https://example.com/contact");

        Assert.False(verdict.IsBlocked);
    }

    [Fact]
    public void Inspect_OrdinaryPage_ReportsClear()
        => Assert.False(_detector.Inspect("<html><body><p>Hello</p></body></html>", "https://example.com/").IsBlocked);

    [Fact]
    public void Inspect_EmptyDocument_ReportsClear()
        => Assert.False(_detector.Inspect(string.Empty, "https://example.com/").IsBlocked);

    /// <summary>
    /// The wall an engine puts up instead of results, which carried none of the earlier signals.
    /// </summary>
    /// <remarks>
    /// Captured on 2026-09-15 from a live engine: 14 228 bytes answered with status <c>202</c> — a SUCCESS —
    /// and no Cloudflare marker, no recaptcha widget and no "unusual traffic" anywhere in it. The word that
    /// does appear sixty-seven times, <c>anomaly</c>, is the bare word this class refuses to carry. What is
    /// matched instead is the sentence, because the asset paths beside it belong to the vendor and will be
    /// renamed while the sentence has to keep meaning what it says.
    /// </remarks>
    [Fact]
    public void Inspect_AnEngineAskingToProveThereIsAHuman_IsBlocked()
    {
        // Разметка воспроизведена как её отдаёт движок: предложение целиком в одном узле и в одной
        // строке — проверено по сохранённому ответу. Перенос внутри фразы её бы не нашёл, и это
        // известная цена подстроки: сигнал переживает смену вёрстки вокруг, но не перенос внутри себя.
        const string wall =
            """
            <!doctype html><html><body>
            <div class="anomaly-modal__title">Unfortunately, bots use DuckDuckGo too.</div>
            <div class="anomaly-modal__description">Please complete the following challenge to confirm this search was made by a human.</div>
            <div class="anomaly-modal__instructions">Select all squares containing a duck:</div>
            </body></html>
            """;

        var verdict = new BlockDetector().Inspect(wall, "https://html.duckduckgo.com/html/?q=postgresql");

        Assert.True(verdict.IsBlocked);
        Assert.Contains("confirm this search was made by a human", verdict.Signal!, StringComparison.Ordinal);
    }

    /// <summary>And a page that merely writes ABOUT such walls is not one, which is what the list costs.</summary>
    [Fact]
    public void Inspect_AnArticleAboutBotWalls_IsNotBlocked()
        => Assert.False(
            new BlockDetector()
                .Inspect(
                    "<html><body><h1>Как устроены проверки на робота</h1><p>Сайты просят решить задачу.</p></body></html>",
                    "https://example.com/blog/anomaly-detection")
                .IsBlocked);
}
