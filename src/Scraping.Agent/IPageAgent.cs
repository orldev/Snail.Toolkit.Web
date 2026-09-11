namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// Walks a page through an errand and returns what it says afterwards.
/// </summary>
public interface IPageAgent
{
    /// <summary>Opens the errand's address, makes every move on it, and reads the resulting page.</summary>
    Task<PageOutcome> PerformAsync(Errand errand, CancellationToken ct = default);
}
