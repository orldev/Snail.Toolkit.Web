using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Tests.Browsers;

/// <summary>
/// A test of something only Chromium has, skipped when the fixtures serve another engine.
/// </summary>
/// <remarks>
/// Client hints are the reason this exists: <c>navigator.userAgentData</c> is Chromium's alone, and a check
/// for it against Firefox fails on the absence of an API nobody promised. Skipping says that out loud, while
/// deleting the check would lose it for the engine that does implement it.
/// <para>
/// The decision is made at discovery because xunit 2.9 has no dynamic skip, the same way
/// <c>LiveFactAttribute</c> does it.
/// </para>
/// </remarks>
public sealed class ChromiumFactAttribute : FactAttribute
{
    /// <summary>Skips the test unless the fixtures are serving Chromium.</summary>
    public ChromiumFactAttribute()
    {
        if (BrowserImage.Engine is not Engine.Chromium)
            Skip = $"Only Chromium has this; the fixtures are serving {BrowserImage.Engine}.";
    }
}
