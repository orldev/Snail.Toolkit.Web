using Microsoft.Extensions.Logging;

namespace Snail.Toolkit.Web;

/// <summary>
/// What the core says about the identities it was given.
/// </summary>
/// <remarks>
/// A misconfiguration that still works is the hardest kind to notice, so both lines here are warnings written once, at start.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message =
        "No fingerprints configured; using {Count} built-in ones. Built-in user agents age into a "
        + "detection signal of their own — set '{Section}:{Property}' and refresh it periodically.")]
    internal static partial void BuiltInFingerprints(ILogger logger, int count, string section, string property);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message =
        "{Count} fingerprint(s) report the UTC time zone. Consumer machines do not run UTC — it is "
        + "what containers and servers default to — and it is measurably refused: from one address, "
        + "with a fresh profile per attempt, Google answered /sorry/ on UTC and returned results on "
        + "both America/New_York and Europe/Moscow. Give '{Section}:{Property}' a plausible zone.")]
    internal static partial void UtcFingerprints(ILogger logger, int count, string section, string property);
}
