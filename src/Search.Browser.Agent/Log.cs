using Microsoft.Extensions.Logging;

namespace Snail.Toolkit.Web.Search.Browser.Agent;

/// <summary>
/// What Google says about its pacing.
/// </summary>
/// <remarks>
/// The pace an engine tolerates was measured, not promised, so a refusal names the gap in force and where to raise it.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message =
        "{Host} is paced at {Named} by configuration, faster than the {Measured} this engine was measured to answer at; the configured gap stands and refusals are to be expected")]
    internal static partial void PacedFaster(ILogger logger, string host, TimeSpan named, TimeSpan measured);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message =
        "{Provider} refused, and the gap in force for {Host} is {Gap}. That default is the shortest "
        + "interval one measurement of four queries answered at, not a rate this engine sustains, and "
        + "what it tolerates depends on the address asking. If refusals persist, raise "
        + "Web:Politeness:HostDelays for that host.")]
    internal static partial void Refused(ILogger logger, string provider, string host, TimeSpan gap);
}
