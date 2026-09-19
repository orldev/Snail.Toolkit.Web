namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// The key under which a search engine is handed its browser: the same pages, without repeating a refusal.
/// </summary>
/// <remarks>
/// A crawl and a search engine read pages the same way and hear a wall differently. A crawl repeats it with a
/// new identity, which is what a wall asks of a reader, and a test holds it to that. A search engine closes
/// over frequency, and the search rests it the moment the refusal reaches it — so the engine is given readers
/// built on attempts that hand a refusal straight back. Keyed rather than a new public parameter, because the
/// difference is a decision this toolkit makes for its own engines and not a knob a consumer should hold.
/// </remarks>
internal static class EngineReading
{
    /// <summary>The service key of the attempts, the reader and the agent kept for search engines.</summary>
    public const string Key = "Snail.Toolkit.Web.Search";
}
