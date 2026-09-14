using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// Resolves where a step lands, by selector or by the caption that names the field.
/// </summary>
/// <remarks>
/// Playwright's own <see cref="IPage.GetByLabel(string, PageGetByLabelOptions)"/> is tried first and is right
/// whenever the markup binds a label to its field. It is not enough on its own: measured on a live registry on
/// 2026-09-12, the caption is a <c>label</c> element carrying no <c>for</c> attribute and no field inside it —
/// the field sits in a sibling container — so that call matched nothing while the first field following the
/// caption matched exactly one. The last candidate drops the requirement that the caption be a <c>label</c> at
/// all, since plenty of forms caption a field with a plain <c>div</c>.
/// </remarks>
internal static class Spots
{
    /// <summary>The element a step is about.</summary>
    public static async Task<ILocator> LocatedAsync(IPage page, Spot spot)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(spot);

        if (spot.Kind is not SpotKind.Label)
            return page.Locator(spot.Value);

        foreach (var candidate in Candidates(page, spot.Value))
        {
            if (await candidate.CountAsync() > 0)
                return candidate;
        }

        return page.GetByLabel(spot.Value);
    }

    private static IEnumerable<ILocator> Candidates(IPage page, string caption)
    {
        yield return page.GetByLabel(caption);
        yield return page.Locator(Follows("label", caption));
        yield return page.Locator(Follows("*", caption));
    }

    /// <summary>The first field that follows a caption, taking the innermost element that carries its text.</summary>
    /// <remarks>
    /// <c>last()</c> is what makes the loose match safe: every ancestor of the caption contains its text too,
    /// and the innermost of them is the caption itself, which is the one whose following field is meant.
    /// </remarks>
    private static string Follows(string tag, string caption)
        => $"xpath=(//{tag}[contains(normalize-space(.), {Quoted(caption)})])[last()]"
           + "/following::*[self::input or self::select or self::textarea][1]";

    /// <summary>Writes a value as an XPath string literal.</summary>
    /// <remarks>
    /// XPath 1.0 has no escape character inside a literal, so a caption carrying both kinds of quote can only
    /// be expressed as a concatenation of the pieces between them.
    /// </remarks>
    private static string Quoted(string text)
    {
        if (!text.Contains('\'', StringComparison.Ordinal))
            return $"'{text}'";

        if (!text.Contains('"', StringComparison.Ordinal))
            return $"\"{text}\"";

        return $"concat({string.Join(", \"'\", ", text.Split('\'').Select(part => $"'{part}'"))})";
    }
}
