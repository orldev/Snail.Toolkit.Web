using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers the memory of what a site left with us — the part of a visit that makes the next one cheaper.
/// </summary>
/// <remarks>
/// A cleared challenge is a cookie, and a context that closes takes it with it. Keeping it is the difference
/// between arriving as a returning visitor and arriving as a stranger who must be challenged again.
/// </remarks>
public class KeepsakesTests
{
    [Fact]
    public void Of_HostThatLeftSomething_AnswersWithIt()
    {
        var kept = Subject(out _);

        kept.Keep("registry.example", "{\"cookies\":[]}");

        Assert.Equal("{\"cookies\":[]}", kept.Of("registry.example"));
    }

    /// <summary>The case of a host name is not part of its identity, as everywhere else in this toolkit.</summary>
    [Fact]
    public void Of_TheSameHostSpelledDifferently_AnswersWithTheSameThing()
    {
        var kept = Subject(out _);

        kept.Keep("Registry.Example", "state");

        Assert.Equal("state", kept.Of("registry.example"));
    }

    /// <summary>A cookie belongs to the site that set it, and nothing here may hand it to another.</summary>
    [Fact]
    public void Of_AnotherHost_AnswersWithNothing()
    {
        var kept = Subject(out _);

        kept.Keep("registry.example", "state");

        Assert.Null(kept.Of("elsewhere.example"));
    }

    [Fact]
    public void Of_HostThatLeftNothing_AnswersWithNothing()
        => Assert.Null(Subject(out _).Of("registry.example"));

    /// <summary>The newest answer replaces the one before it rather than joining it.</summary>
    [Fact]
    public void Keep_TwiceForOneHost_KeepsTheNewer()
    {
        var kept = Subject(out _);

        kept.Keep("registry.example", "first");
        kept.Keep("registry.example", "second");

        Assert.Equal("second", kept.Of("registry.example"));
    }

    /// <summary>An empty state is not an answer, and storing it would forget a real one.</summary>
    [Fact]
    public void Keep_AnEmptyState_ChangesNothing()
    {
        var kept = Subject(out _);

        kept.Keep("registry.example", "state");
        kept.Keep("registry.example", "   ");

        Assert.Equal("state", kept.Of("registry.example"));
    }

    /// <summary>
    /// A host nobody goes back to stops taking up room, on the same rule as every other per-host memory here.
    /// </summary>
    [Fact]
    public void Of_HostNobodyReturnedToWithinTheWindow_IsForgotten()
    {
        var kept = Subject(out var clock, TimeSpan.FromMinutes(10));

        kept.Keep("registry.example", "state");

        clock.Advance(TimeSpan.FromMinutes(31));

        // Forgetting happens when an unknown host arrives, which is the only moment the map grows.
        kept.Keep("elsewhere.example", "other");

        Assert.Null(kept.Of("registry.example"));
        Assert.Equal("other", kept.Of("elsewhere.example"));
    }

    /// <summary>And a host that is asked about keeps its place, because asking is the evidence of a visit.</summary>
    [Fact]
    public void Of_HostVisitedAgainInsideTheWindow_IsKept()
    {
        var kept = Subject(out var clock, TimeSpan.FromMinutes(10));

        kept.Keep("registry.example", "state");

        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal("state", kept.Of("registry.example"));

        clock.Advance(TimeSpan.FromMinutes(6));
        kept.Keep("elsewhere.example", "other");

        Assert.Equal("state", kept.Of("registry.example"));
    }

    private static Keepsakes Subject(out FakeTimeProvider clock, TimeSpan? window = null)
    {
        clock = new FakeTimeProvider();

        return new Keepsakes(
            Options.Create(new ScrapingOptions { RememberSiteFor = window ?? TimeSpan.FromHours(1) }),
            clock);
    }
}
