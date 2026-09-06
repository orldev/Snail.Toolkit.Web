namespace Snail.Toolkit.Web.Detection;

/// <summary>
/// Whether a fetched page is the page that was asked for, and what gave it away when it is not.
/// </summary>
/// <param name="IsBlocked">Whether the site answered with a challenge or a refusal.</param>
/// <param name="Signal">The indicator that matched, for logs and metrics.</param>
/// <remarks>
/// Detection used to answer with a bare <see cref="bool"/>, so a log could record that a page was refused but
/// never which wall it hit — the one fact that says whether to rotate the proxy, slow down, or accept a
/// consent form.
/// </remarks>
public sealed record BlockVerdict(bool IsBlocked, string? Signal)
{
    /// <summary>The verdict of a page that showed no sign of a block.</summary>
    public static BlockVerdict Clear { get; } = new(false, null);

    /// <summary>The verdict of a page that was refused, and what matched.</summary>
    public static BlockVerdict Blocked(string signal) => new(true, signal);
}
