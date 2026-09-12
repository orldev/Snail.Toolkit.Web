namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// What the page looked like at the moment a step gave up on it.
/// </summary>
/// <remarks>
/// A fruitless step used to report only which move produced nothing, which is the question the errand already
/// answers; the one left open is what the page held instead. Answering it by hand on 2026-09-12 took five
/// throwaway scripts — how many elements the address matched, whether any were visible, what the page offered
/// instead, whether a request went out at all — and every one of those answers is in reach of the agent at the
/// instant it fails.
/// </remarks>
internal sealed record Scene(int Matched, int Visible, string Url, string Title)
{
    /// <summary>The editable fields the page actually offers.</summary>
    public IReadOnlyList<string> Fields { get; init; } = [];

    /// <summary>What the page asked the site for while the step was being attempted.</summary>
    public IReadOnlyList<string> Asked { get; init; } = [];

    /// <inheritdoc />
    public override string ToString()
        => $"At that point {Addressed()}; the page was '{Title}' at {Url}, {Offered()}, and {Requested()}.";

    private string Addressed() => Matched switch
    {
        0 => "nothing on the page matched that address",
        _ when Visible == 0 => $"{Matched} elements matched it and none of them were visible",
        _ => $"{Matched} elements matched it, {Visible} of them visible"
    };

    private string Offered()
        => Fields.Count == 0
            ? "it offers no editable field"
            : $"it offers {string.Join(", ", Fields)}";

    private string Requested()
        => Asked.Count == 0
            ? "the page asked the site for nothing"
            : $"the page asked for {string.Join(", ", Asked)}";
}
