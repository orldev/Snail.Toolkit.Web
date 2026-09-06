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

        if (text.Length <= MaxCharacters)
            return new BudgetedText(text, false, text.Length);

        var kept = text[..MaxCharacters];

        return new BudgetedText(
            $"{kept}\n\n{Marker}: {MaxCharacters} of {text.Length} characters",
            true,
            text.Length);
    }
}
