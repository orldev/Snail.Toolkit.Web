namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// What a read answers about a page beside its markup, and whether it hands over the document it parsed.
/// </summary>
/// <remarks>
/// A page was parsed once for its text, once for its title, and again by whoever read it next: a crawl for the
/// page's links and its canonical address, an engine for its results. The document a read parsed now travels on
/// to that reader, which parses nothing — but only to a reader inside this library, and only on the roads that
/// need it, because a parsed document is twenty times the markup and a consumer keeping its pages would keep
/// every one of them.
/// </remarks>
internal enum Reading
{
    /// <summary>The text and the title, as every page read answers them.</summary>
    Text,

    /// <summary>The text and the title, and the document they were read from, for a crawl.</summary>
    TextAndDocument,

    /// <summary>The document alone, for a search engine, which reads its results out of it and nothing else.</summary>
    DocumentOnly
}
