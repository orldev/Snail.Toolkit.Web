using Microsoft.Extensions.Logging;

namespace Snail.Toolkit.Web.Search;

/// <summary>
/// What a search says about each provider it asked.
/// </summary>
/// <remarks>
/// One line per provider per query, at the level the outcome deserves: a refusal or a failure is a warning or an error, an answer is information, nothing found is debug.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message =
        "{Provider} answered {Length} characters of markup that yielded no results, which is what a "
        + "layout change looks like from here; the caller is told nothing was found.")]
    internal static partial void NoResults(ILogger logger, string provider, int length);

    [LoggerMessage(EventId = 2, Message =
        "{Resting} left out of this search, resting after a refusal; {Count} provider(s) asked")]
    internal static partial void Resting(ILogger logger, LogLevel level, string resting, int count);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message =
        "{Provider} ran past the budget for '{Query}' after {Ms:F0}ms")]
    internal static partial void OverBudget(ILogger logger, string provider, string query, double ms);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message =
        "{Provider} was cancelled for '{Query}' after {Ms:F0}ms")]
    internal static partial void Cancelled(ILogger logger, string provider, string query, double ms);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message =
        "{Provider} threw for '{Query}' after {Ms:F0}ms")]
    internal static partial void Threw(ILogger logger, Exception exception, string provider, string query, double ms);

    [LoggerMessage(EventId = 6, Level = LogLevel.Debug, Message =
        "{Provider} found nothing for '{Query}' in {Ms:F0}ms")]
    internal static partial void FoundNothing(ILogger logger, string provider, string query, double ms);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message =
        "{Provider} found {Count} links for '{Query}' in {Ms:F0}ms")]
    internal static partial void Found(ILogger logger, string provider, int count, string query, double ms);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error, Message =
        "{Provider} failed for '{Query}' after {Ms:F0}ms: {Reason}")]
    internal static partial void Failed(ILogger logger, string provider, string query, double ms, string? reason);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message =
        "{Provider} refused '{Query}' after {Ms:F0}ms and is left alone for {Rest}: {Reason}")]
    internal static partial void RefusedQuery(ILogger logger, string provider, string query, double ms, TimeSpan rest, string? reason);
}
