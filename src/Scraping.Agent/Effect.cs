namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// What must become true for a step to count as having happened.
/// </summary>
/// <remarks>
/// This type is the whole point of the package. Measured on a live registry on 2026-09-11: filling a field
/// through Playwright's <c>fill</c> set its value — the field read back exactly what was typed — and the site
/// never learned of it, because its suggestions hang off key events that a value assignment does not raise.
/// Worse, typing key by key raised them on one run and not on the next, with identical code. A step that only
/// reports whether it executed is therefore worthless: success and silence look the same. A step reports
/// whether the site reacted.
/// </remarks>
public sealed record Effect(EffectKind Kind, string Pattern)
{
    /// <summary>How long the consequence is waited for before the step is called fruitless.</summary>
    public TimeSpan Within { get; init; } = TimeSpan.FromSeconds(5);
}
