namespace Snail.Toolkit.Web.Text;

/// <summary>
/// Text that has been through a <see cref="ContentBudget"/>.
/// </summary>
/// <param name="Text">The content, carrying <see cref="ContentBudget.Marker"/> when it was cut.</param>
/// <param name="IsTruncated">Whether anything was dropped.</param>
/// <param name="OriginalLength">How long the content was before the budget was applied.</param>
/// <remarks>
/// The flag is carried beside the text rather than left to be inferred from the marker, so a caller can branch
/// on the fact — fetch the rest, widen the budget, warn the reader — without parsing prose.
/// </remarks>
public sealed record BudgetedText(string Text, bool IsTruncated, int OriginalLength);
