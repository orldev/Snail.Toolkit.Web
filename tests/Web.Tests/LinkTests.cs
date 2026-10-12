namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the seam between the two engines: if these two agree on nothing else, they agree here.
/// </summary>
public class LinkTests
{
    /// <summary>
    /// Deduplication is the reason several providers are queried at once, and grouping on the raw string did
    /// not do it: the same page arrives in all of these shapes.
    /// </summary>
    [Theory]
    [InlineData("http://Example.com/a", "https://example.com/a")]
    [InlineData("https://example.com/a/", "https://example.com/a")]
    [InlineData("https://example.com/a#section", "https://example.com/a")]
    [InlineData("https://example.com/a?utm_source=ddg&x=1", "https://example.com/a?x=1")]
    [InlineData("https://example.com/a?gclid=123", "https://example.com/a")]
    public void Of_EquivalentAddresses_ProduceOneCanonicalForm(string url, string expected)
    {
        Assert.Equal(expected, Link.Of(url).Canonical);
    }

    /// <summary>
    /// The canonical form is concatenated from the parsed parts where a second parse could change nothing, and
    /// built where it could; these are the addresses on either side of that line, with the form both ways answer.
    /// </summary>
    [Theory]
    [InlineData("http://Example.COM/a/b/?utm_source=x&b=2#frag", "https://example.com/a/b?b=2")]
    [InlineData("http://x.com:443/a", "https://x.com/a")]
    [InlineData("https://x.com:80/a/", "https://x.com:80/a")]
    [InlineData("https://x.com/??a=1", "https://x.com/?a=1")]
    [InlineData("https://[::1]/a/", "https://[::1]/a")]
    [InlineData("https://x.com/a?a=1&utm_term=2&c=3", "https://x.com/a?a=1&c=3")]
    [InlineData("https://x.com//", "https://x.com/")]
    [InlineData("https://x.com/a?&&", "https://x.com/a")]
    [InlineData("https://bücher.example/a/", "https://bücher.example/a")]
    [InlineData("https://x.com/a/.../", "https://x.com/a/...")]
    public void Of_AddressesEitherSideOfTheConcatenation_AnswerTheBuiltForm(string url, string expected)
    {
        Assert.Equal(expected, Link.Of(url).Canonical);
    }

    [Fact]
    public void Of_DistinctPaths_KeepsThemApart()
    {
        Assert.NotEqual(Link.Of("https://example.com/a"), Link.Of("https://example.com/b"));
    }

    /// <summary>Equality is the whole point: search groups by it and scraping throttles by it.</summary>
    [Fact]
    public void Equals_SchemeAndTrailingSlashDiffer_ReportsSameLink()
    {
        Assert.Equal(Link.Of("http://Example.com/a/"), Link.Of("https://example.com/a"));
    }

    [Fact]
    public void GetHashCode_EquivalentAddresses_AgreeSoASetDeduplicates()
    {
        var links = new HashSet<Link> { Link.Of("http://example.com/a"), Link.Of("https://example.com/a/") };

        Assert.Single(links);
    }

    [Fact]
    public void Of_SomethingThatIsNotAnAddress_KeepsItAndSaysItIsNotAbsolute()
    {
        var link = Link.Of("  not a url  ");

        Assert.Equal("not a url", link.Canonical);
        Assert.False(link.IsAbsolute);
        Assert.Null(link.Address);
    }

    [Fact]
    public void Of_NonHttpScheme_IsLeftAlone()
    {
        Assert.False(Link.Of("mailto:someone@example.com").IsAbsolute);
    }

    [Fact]
    public void Of_NonDefaultPort_KeepsIt()
    {
        Assert.Equal("https://example.com:8443/a", Link.Of("https://example.com:8443/a").Canonical);
    }

    [Fact]
    public void ToString_AlwaysRendersTheAddressAsWritten()
    {
        Assert.Equal("http://Example.com/a/", Link.Of("http://Example.com/a/").ToString());
    }

    /// <summary>A password written into an address must not survive into anything that is read.</summary>
    /// <remarks>
    /// <see cref="Link.Canonical"/> is the key a crawl deduplicates by and the value the reader puts on the
    /// <c>url.full</c> span tag, and <see cref="Link.ToString"/> is what a log line prints. Until 2026-09-13
    /// credentials reached all three, so one link carrying them published them to the tracing backend and to
    /// every log that named the link.
    /// </remarks>
    [Theory]
    [InlineData("https://user:secret@example.com/a")]
    [InlineData("http://user:secret@example.com:8443/a")]
    [InlineData("https://user@example.com/a")]
    public void Of_AddressCarryingCredentials_KeepsThemOutOfEveryFormThatIsRead(string url)
    {
        var link = Link.Of(url);

        Assert.DoesNotContain("secret", link.Canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", link.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("user", link.Canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("user", link.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The transport still gets the credentials, because it is the one thing that needs them.</summary>
    [Fact]
    public void Of_AddressCarryingCredentials_LeavesThemOnTheParsedAddress()
    {
        Assert.Equal("user:secret", Link.Of("https://user:secret@example.com/a").Address!.UserInfo);
    }

    /// <summary>Credentials are not part of a page's identity: the same page is the same link with or without them.</summary>
    [Fact]
    public void Of_TheSamePageWithAndWithoutCredentials_IsOneLink()
    {
        Assert.Equal(Link.Of("https://example.com/a"), Link.Of("https://user:secret@example.com/a"));
    }
}
