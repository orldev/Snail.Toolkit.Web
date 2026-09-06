using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Snail.Toolkit.Web.Diagnostics;

/// <summary>
/// The activity source and meter every package in this toolkit publishes under.
/// </summary>
/// <remarks>
/// Engines whose normal failure mode used to be an empty string had no way to be watched: nothing counted
/// blocks, nothing timed a fetch, and a crawl that quietly returned nothing looked identical to one that
/// succeeded. One name is shared across the packages so a consumer subscribes once and sees the whole toolkit;
/// the instruments themselves belong to whichever package measures something.
/// </remarks>
public static class WebDiagnostics
{
    /// <summary>The name to subscribe to for traces and metrics.</summary>
    public const string SourceName = "Snail.Toolkit.Web";

    /// <summary>The source spans are recorded on.</summary>
    public static ActivitySource Activity { get; } = new(SourceName);

    /// <summary>The meter instruments are created from.</summary>
    public static Meter Meter { get; } = new(SourceName);
}
