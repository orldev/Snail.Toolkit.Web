using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Holds the read tail to naming a refused page for what it is, rather than for what it looks like.
/// </summary>
/// <remarks>
/// The ceiling on nesting depth lives in the core and raises <see cref="MarkupTooDeepException"/>. This is the
/// one place that catches it, and the whole reason it does is that every other outcome available here is a
/// statement about the SITE: a page refused by us would otherwise reach a caller as
/// <see cref="FailureKind.Empty"/> — "the page rendered no content" — about a page that rendered plenty.
/// <para>
/// The distinction matters beyond wording, because a caller branches on the kind. A crawl that sees
/// <see cref="FailureKind.Empty"/> has learned something about the site and moves on satisfied; one that sees
/// <see cref="FailureKind.Unreadable"/> has learned something about this library, and the difference decides
/// whether anybody ever goes looking.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class UnreadableMarkupTests
{
    /// <summary>Markup past the ceiling comes back named, with the page's own status kept.</summary>
    /// <remarks>
    /// The status and the identity are checked alongside the kind because this failure is built by hand on a
    /// path where the others are not, and a hand-built failure is exactly the kind that quietly forgets a
    /// field. A <c>200</c> that arrives as <see langword="null"/> would make the page look like it never
    /// answered.
    /// </remarks>
    [Fact]
    public void Judged_MarkupNestedDeeperThanItMayBe_IsNamedUnreadableRatherThanEmpty()
    {
        var outcome = Contents().Judged(Page(Nested(5_000)));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(FailureKind.Unreadable, outcome.Failure.Kind);
        Assert.Equal(200, outcome.Failure.StatusCode);
        Assert.Equal("Agent/1", outcome.Failure.UserAgent);
        Assert.IsType<MarkupTooDeepException>(outcome.Failure.Cause);
    }

    /// <summary>A refused page is not worth asking for again.</summary>
    /// <remarks>
    /// The retry pipeline is driven by <see cref="PageFailure.IsTransient"/>, and a new kind lands in its
    /// default arm — so this asserts the default is the right one rather than assuming it. Retrying would
    /// spend the whole budget re-parsing markup that will be shaped exactly the same way next time, and the
    /// gap between the attempts would be spent waiting rather than being told.
    /// </remarks>
    [Fact]
    public void Judged_MarkupNestedDeeperThanItMayBe_IsNotWorthRepeating()
    {
        var outcome = Contents().Judged(Page(Nested(5_000)));

        Assert.False(outcome.Failure!.IsTransient);
    }

    /// <summary>A page inside the ceiling is still read, which is the half a guard gets wrong.</summary>
    [Fact]
    public void Judged_MarkupInsideTheCeiling_IsStillRead()
    {
        var outcome = Contents().Judged(Page("<html><body><h1>Название</h1><p>Текст страницы.</p></body></html>"));

        Assert.True(outcome.IsSuccess);
        Assert.Equal("Название", outcome.Value.Title);
        Assert.Contains("Текст страницы.", outcome.Value.Text, StringComparison.Ordinal);
    }

    private static PageContents Contents()
        => new(new TextExtraction(), Options.Create(new ScrapingOptions()));

    private static Document Page(string html) => new()
    {
        Address = Link.Of("https://example.com/a"),
        Identity = Identity(),
        Html = html,
        Rendered = "hello",
        FinalUrl = "https://example.com/a",
        StatusCode = 200
    };

    /// <summary>Rendered text is non-empty on purpose: emptiness is judged on it before the markup is touched.</summary>
    private static string Nested(int depth)
        => string.Concat(Enumerable.Repeat("<div>", depth))
           + "hello"
           + string.Concat(Enumerable.Repeat("</div>", depth));

    private static WebIdentity Identity() => new()
    {
        UserAgent = "Agent/1",
        Platform = "Windows",
        NavigatorPlatform = "Win32",
        Brand = "Google Chrome",
        MajorVersion = "124",
        Locale = "en-US",
        Timezone = "UTC",
        AcceptLanguage = "en-US,en;q=0.9",
        Languages = ["en-US", "en"]
    };
}
