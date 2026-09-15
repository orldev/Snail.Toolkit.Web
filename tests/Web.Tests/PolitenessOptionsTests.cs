using Snail.Toolkit.Web.Politeness;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Covers the rule <see cref="PolitenessOptions.HostDelays"/> states: which key covers which host, and how
/// "nobody named a gap here" is said.
/// </summary>
/// <remarks>
/// The gate's own behaviour is covered next door; what is checked here is the answer a second caller needs.
/// A package that paces its own engine writes a gap only where the deployment named none, and until
/// 2026-09-15 it asked that question with an exact-key lookup — which reads a covering key as silence.
/// </remarks>
public class PolitenessOptionsTests
{
    [Fact]
    public void GapFor_HostBeneathANamedOne_TakesTheNamedGap()
        => Assert.Equal(
            TimeSpan.FromMinutes(5),
            Named(("example.com", TimeSpan.FromMinutes(5))).GapFor("www.example.com"));

    /// <summary>The most specific key wins, which is what lets one subdomain be treated apart from its siblings.</summary>
    [Fact]
    public void GapFor_HostNamedBesideItsParent_TakesItsOwnGap()
        => Assert.Equal(
            TimeSpan.FromSeconds(1),
            Named(
                ("example.com", TimeSpan.FromMinutes(5)),
                ("www.example.com", TimeSpan.FromSeconds(1))).GapFor("www.example.com"));

    /// <summary>The cut is at a dot, so a name that merely ends the same way is not a parent.</summary>
    [Fact]
    public void GapFor_HostThatMerelyEndsLikeANamedOne_AnswersNothing()
        => Assert.Null(Named(("ample.com", TimeSpan.FromMinutes(5))).GapFor("example.com"));

    /// <summary>
    /// Nothing, not <see cref="PolitenessOptions.MinDelayPerHost"/> — the two answers send a caller in
    /// opposite directions, and the floor cannot tell an unnamed host from one named at exactly the default.
    /// </summary>
    [Fact]
    public void GapFor_HostNobodyNamed_AnswersNothing()
        => Assert.Null(Named(("other.example", TimeSpan.FromMinutes(5))).GapFor("www.example.com"));

    /// <summary>A host named at exactly the default is still a host the deployment named.</summary>
    [Fact]
    public void GapFor_HostNamedAtTheDefaultGap_AnswersThatGapRatherThanNothing()
    {
        var options = Named(("example.com", TimeSpan.FromSeconds(1)));

        Assert.Equal(options.MinDelayPerHost, options.GapFor("example.com"));
    }

    /// <summary>
    /// Host names are case-insensitive, and the comparison is made here rather than left to the dictionary:
    /// the default instance ignores case and one arriving from configuration binding is not promised to.
    /// </summary>
    [Fact]
    public void GapFor_NameWrittenInAnotherCase_IsStillFound()
    {
        var options = new PolitenessOptions
        {
            HostDelays = new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
            {
                ["Example.COM"] = TimeSpan.FromMinutes(5)
            }
        };

        Assert.Equal(TimeSpan.FromMinutes(5), options.GapFor("WWW.example.com"));
    }

    private static PolitenessOptions Named(params (string Host, TimeSpan Gap)[] gentler)
        => new()
        {
            HostDelays = gentler.ToDictionary(named => named.Host, named => named.Gap, StringComparer.OrdinalIgnoreCase)
        };
}
