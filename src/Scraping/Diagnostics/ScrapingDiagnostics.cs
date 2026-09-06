using System.Diagnostics.Metrics;
using Snail.Toolkit.Web.Diagnostics;

namespace Snail.Toolkit.Web.Scraping.Diagnostics;

/// <summary>
/// The instruments this package records, on the toolkit's shared meter.
/// </summary>
/// <remarks>
/// An engine whose normal failure mode used to be an empty string had no way to be watched: nothing counted
/// blocks, nothing timed a fetch, and a crawl that quietly returned nothing looked identical to one that
/// succeeded. Subscribe to <see cref="WebDiagnostics.SourceName"/> for these.
/// </remarks>
internal static class ScrapingDiagnostics
{
    public static readonly Counter<long> Pages = WebDiagnostics.Meter.CreateCounter<long>(
        "web.scraping.pages", "{page}", "Pages read, tagged by outcome and host.");

    public static readonly Histogram<double> Duration = WebDiagnostics.Meter.CreateHistogram<double>(
        "web.scraping.duration", "ms", "How long a page read took, tagged by outcome.");
}
