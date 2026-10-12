using Microsoft.Extensions.Logging;

namespace Snail.Toolkit.Web.Search.Browser;

/// <summary>
/// What a browser-backed engine says about the pages it was handed.
/// </summary>
/// <remarks>
/// Markup that yields no results is what a layout change looks like from here, so it is a warning.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message =
        "{Provider} answered with {Length} characters of markup that yielded no results, which is what "
        + "a layout change looks like from here.")]
    internal static partial void NoResults(ILogger logger, string provider, int length);
}
