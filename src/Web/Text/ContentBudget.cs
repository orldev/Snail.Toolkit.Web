namespace Snail.Toolkit.Web.Text;

/// <summary>
/// A ceiling on how much text may travel back to a caller, with any cut stated in the text itself.
/// </summary>
/// <remarks>
/// Neither engine this was assembled from had a limit at all: a five-megabyte page went back whole, and the
/// caller that hurts most is an agent, whose context window it fills before it can read a word. Silent
/// truncation is worse than none — a model given half a document with no sign of the cut answers confidently
/// about a page it has not seen — so the marker is part of the text and names both lengths.
/// </remarks>
public sealed record ContentBudget
{
    /// <summary>The words that announce a cut. Present verbatim in every truncated result.</summary>
    public const string Marker = "[content truncated]";

    private ContentBudget(int maxCharacters) => MaxCharacters = maxCharacters;

    /// <summary>A budget that never cuts.</summary>
    public static ContentBudget Unlimited { get; } = new(int.MaxValue);

    /// <summary>Gets how many characters of content survive.</summary>
    public int MaxCharacters { get; }

    /// <summary>Reads a budget of a given size.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The size is zero or negative.</exception>
    public static ContentBudget Of(int maxCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        return new ContentBudget(maxCharacters);
    }

    /// <summary>Applies the budget, appending the marker when anything was dropped.</summary>
    public BudgetedText Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Apply(text, text.Length);
    }

    /// <summary>Applies the budget to text that has already been through one, naming the length it really had.</summary>
    /// <remarks>
    /// A crawl budgets what the reader budgeted, because reading one page and reading a site are worth
    /// different amounts of text. Applying a second ceiling to the first one's output produced a marker that
    /// described the first cut rather than the page: measured on 2026-09-13, nine hundred characters came back
    /// announced as "60 of 164", and a model reading that believes it saw a third of a short page rather than
    /// a fifteenth of a long one. The earlier announcement is stripped before the cut rather than after, so
    /// the ceiling can never land in the middle of it and leave half a sentence claiming a length.
    /// </remarks>
    public BudgetedText Apply(string text, int originalLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(originalLength);

        var said = Announced(text);
        var original = Math.Max(originalLength, said.Length);

        if (said.Length > MaxCharacters)
            return new BudgetedText(Marked(said[..MaxCharacters], original), true, original);

        return said.Length == original
            ? new BudgetedText(said, false, original)
            : new BudgetedText(Marked(said, original), true, original);
    }

    private static string Marked(string kept, int original)
        => $"{kept}\n\n{Marker}: {kept.Length} of {original} characters";

    /// <summary>The text without the announcement a previous budget left on the end of it.</summary>
    private static string Announced(string text)
    {
        var marker = text.LastIndexOf($"\n\n{Marker}: ", StringComparison.Ordinal);

        return marker < 0 ? text : text[..marker];
    }
}
