namespace Snail.Toolkit.Web.Search;

/// <summary>
/// One link a provider offered, with the words it offered them under.
/// </summary>
/// <remarks>
/// The address is a <see cref="Link"/> rather than a string, which is what makes deduplication across
/// providers work at all: the same page arrives from two of them as http and https, with and without a
/// trailing slash, and carrying whichever campaign parameters each attached.
/// There is deliberately no page content here. The moment this record carries what a page says, this package
/// and the scraping one are the same package under two names.
/// </remarks>
public sealed record SearchResult
{
    /// <summary>Gets the title the provider showed for the link.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the address the link points at.</summary>
    public required Link Address { get; init; }

    /// <summary>Gets the provider's own summary of the page — never the page itself.</summary>
    public required string Snippet { get; init; }

    /// <summary>Gets the provider that offered the link.</summary>
    public required string Provider { get; init; }
}
