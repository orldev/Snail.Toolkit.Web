using Microsoft.Extensions.Logging;

namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// What the page agent says about the steps it took.
/// </summary>
/// <remarks>
/// A step that produced nothing is the one the caller has to act on, so it is the one warning; the moves themselves are debug.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message =
        "Attempt {Attempt} at {Kind} on {Where} left the site unchanged")]
    internal static partial void LeftUnchanged(ILogger logger, int attempt, StepKind kind, Spot where);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message =
        "{Kind} on {Where} did not go through")]
    internal static partial void NotThrough(ILogger logger, Exception exception, StepKind kind, Spot where);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message =
        "Step {Position} of {Total} produced nothing. {Scene}")]
    internal static partial void ProducedNothing(ILogger logger, int position, int total, Scene scene);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message =
        "The page would not mint a '{Action}' credential, so its service was never asked")]
    internal static partial void NotMinted(ILogger logger, string action);
}
