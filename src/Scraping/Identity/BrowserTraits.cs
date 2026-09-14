using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping.Identity;

/// <summary>
/// What a browser reports that an HTTP client has nothing to say about.
/// </summary>
/// <remarks>
/// These traits used to sit on the shared identity, where they were dead weight for a transport with no screen
/// and no pointer — and worse than dead weight, because a value nobody can honour is a claim waiting to
/// contradict the rest of the fingerprint. They are derived from the identity rather than chosen beside it, so
/// the scale factor and the touch flag still agree with the user agent.
/// </remarks>
public sealed record BrowserTraits
{
    private static readonly (int Width, int Height)[] Viewports =
    [
        (1920, 1080),
        (1366, 768),
        (1440, 900),
        (1536, 864)
    ];

    /// <summary>Gets the viewport width in CSS pixels.</summary>
    public required int ViewportWidth { get; init; }

    /// <summary>Gets the viewport height in CSS pixels.</summary>
    public required int ViewportHeight { get; init; }

    /// <summary>Gets the device pixel ratio.</summary>
    public required float DeviceScaleFactor { get; init; }

    /// <summary>Gets whether the identity describes a touch device.</summary>
    public required bool HasTouch { get; init; }

    /// <summary>The traits this identity reports, the same ones every time it is presented.</summary>
    /// <remarks>
    /// The viewport is chosen by the user agent rather than drawn per visit, which is the correction made on
    /// 2026-09-14. A random draw gave one identity a different screen on every visit — the same browser, on the
    /// same machine, resized between two page loads — and that is a contradiction of exactly the kind this type
    /// was extracted to end. People do resize windows, but the pairing of a screen with a machine is the stable
    /// part, and an identity is a machine.
    /// <para>
    /// One call answers both the context and the injected script, so a page's window size cannot disagree with
    /// what its scripts see. That held before by reusing one draw and holds now by construction.
    /// </para>
    /// </remarks>
    public static BrowserTraits For(WebIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var viewport = Viewports[Steadily(identity.UserAgent) % Viewports.Length];

        return new BrowserTraits
        {
            ViewportWidth = viewport.Width,
            ViewportHeight = viewport.Height,
            DeviceScaleFactor = identity.Platform is "macOS" ? 2f : 1f,
            HasTouch = identity.UserAgent.Contains("Mobile", StringComparison.Ordinal)
        };
    }

    /// <summary>A number the same user agent answers with on any machine, in any process, forever.</summary>
    /// <remarks>
    /// <see cref="string.GetHashCode()"/> is randomised per process, so it would give one identity one screen
    /// today and another tomorrow — the very thing being fixed, moved from per visit to per run. FNV-1a is four
    /// lines and answers the same everywhere.
    /// </remarks>
    private static int Steadily(string userAgent)
    {
        var hash = 2166136261u;

        foreach (var character in userAgent)
            hash = (hash ^ character) * 16777619u;

        return (int)(hash % int.MaxValue);
    }
}
