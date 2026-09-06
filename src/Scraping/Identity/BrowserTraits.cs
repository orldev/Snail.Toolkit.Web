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

    /// <summary>Draws the traits one visit is made with.</summary>
    /// <remarks>
    /// The viewport is picked per visit, so the same result has to be used for both the context and the
    /// injected script: drawing twice would give a page whose window size disagrees with what its scripts see.
    /// </remarks>
    public static BrowserTraits For(WebIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var viewport = Viewports[Random.Shared.Next(Viewports.Length)];

        return new BrowserTraits
        {
            ViewportWidth = viewport.Width,
            ViewportHeight = viewport.Height,
            DeviceScaleFactor = identity.Platform is "macOS" ? 2f : 1f,
            HasTouch = identity.UserAgent.Contains("Mobile", StringComparison.Ordinal)
        };
    }
}
