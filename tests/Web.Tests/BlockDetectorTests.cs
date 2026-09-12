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

    [Fact]
    public void Inspect_OrdinaryPage_ReportsClear()
        => Assert.False(_detector.Inspect("<html><body><p>Hello</p></body></html>", "https://example.com/").IsBlocked);

    [Fact]
    public void Inspect_EmptyDocument_ReportsClear()
        => Assert.False(_detector.Inspect(string.Empty, "https://example.com/").IsBlocked);
}
