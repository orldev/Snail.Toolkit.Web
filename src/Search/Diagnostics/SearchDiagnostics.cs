using System.Diagnostics.Metrics;
using Snail.Toolkit.Web.Diagnostics;

namespace Snail.Toolkit.Web.Search.Diagnostics;

/// <summary>
/// The instruments this package records, on the toolkit's shared meter.
/// </summary>
/// <remarks>
/// These replace the hand-rolled monitor this package was assembled from, which kept success counts and a
/// rolling latency average in a dictionary updated from parallel workers without a lock — a race that made the
/// health it reported wrong in exactly the conditions it existed to describe.
/// </remarks>
internal static class SearchDiagnostics
{
    public static readonly Counter<long> Attempts = WebDiagnostics.Meter.CreateCounter<long>(
        "web.search.attempts", "{attempt}", "Provider attempts, tagged by provider and outcome.");

    public static readonly Histogram<double> Duration = WebDiagnostics.Meter.CreateHistogram<double>(
        "web.search.duration", "ms", "How long a provider attempt took, tagged by provider and outcome.");
}
