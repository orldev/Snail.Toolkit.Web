using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Snail.Toolkit.Web.Gen;

/// <summary>
/// Turns one documentation element into Markdown, keeping what the author meant and dropping the ceremony.
/// </summary>
/// <remarks>
/// Deliberately small. A full translator would handle everything the xml documentation format allows; this
/// handles what this repository actually writes, which is a much shorter list, and leaves the text alone
/// otherwise. The failure mode of a thorough translator here would be worse than of a modest one: prose that
/// quietly loses a clause reads as finished.
/// </remarks>
internal static partial class Prose
{
    /// <summary>The element as Markdown, or nothing when it holds nothing worth printing.</summary>
    internal static string? Of(XElement? element)
    {
        if (element is null)
            return null;

        var text = Written(element).Trim();

        return text.Length > 0 ? Tidied(text) : null;
    }

    /// <remarks>
    /// A paragraph is a blank line, not a tag: everything downstream is Markdown.
    /// <para>
    /// A cref names a declaration; the reader wants the name, not the assembly-qualified id.
    /// </para>
    /// </remarks>
    private static string Written(XNode node)
    {
        switch (node)
        {
            case XText text:
                return text.Value;

            case not XElement:
                return string.Empty;
        }

        var element = (XElement)node;
        var inside = string.Concat(element.Nodes().Select(Written));

        return element.Name.LocalName switch
        {
            "para" => $"\n\n{inside.Trim()}\n\n",

            "c" => $"`{inside.Trim()}`",

            "see" or "seealso" => Named(element, inside),
            "paramref" or "typeparamref" => $"`{element.Attribute("name")?.Value}`",

            "code" => $"\n\n```csharp\n{Dented(inside)}\n```\n\n",

            "list" => $"\n\n{Listed(element)}\n",
            "item" or "description" or "term" => inside,

            "b" or "strong" => $"**{inside.Trim()}**",
            "i" or "em" => $"*{inside.Trim()}*",

            _ => inside
        };
    }

    /// <summary>What a cref or a langword should read as once the prefix and the namespace are gone.</summary>
    private static string Named(XElement element, string inside)
    {
        if (inside.Trim() is { Length: > 0 } written)
            return written.Trim();

        if (element.Attribute("langword")?.Value is { Length: > 0 } langword)
            return $"`{langword}`";

        if (element.Attribute("cref")?.Value is not { Length: > 2 } cref)
            return element.Attribute("href")?.Value ?? string.Empty;

        var body = cref[2..].Split('(')[0];
        var cut = body.LastIndexOf('.');
        var name = (cut < 0 ? body : body[(cut + 1)..]).Split('`')[0];

        return $"`{name}`";
    }

    private static string Listed(XElement list)
    {
        var bullet = list.Attribute("type")?.Value is "number" ? null : "- ";
        var rows = new StringBuilder();
        var at = 1;

        foreach (var item in list.Elements("item"))
        {
            var term = Written(item.Element("term") ?? new XElement("term")).Trim();
            var said = Written(item.Element("description") ?? item).Trim();
            var line = term.Length > 0 ? $"**{term}** — {said}" : said;

            rows.Append(bullet ?? $"{at++}. ").Append(Flattened(line)).Append('\n');
        }

        return rows.ToString().TrimEnd();
    }

    /// <summary>Removes the indentation the xml file carries, which is the file's shape and not the code's.</summary>
    private static string Dented(string block)
    {
        var lines = block.Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n').Split('\n');

        var common = lines
            .Where(line => line.Trim().Length > 0)
            .Select(line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join('\n', lines.Select(line => line.Length >= common ? line[common..] : line.TrimStart()));
    }

    /// <summary>A list row has to be one line, or Markdown reads the wrapped part as a new paragraph.</summary>
    private static string Flattened(string line) => Spaces().Replace(line, " ").Trim();

    /// <summary>
    /// Collapses the line breaks the xml inherited from the source while keeping the breaks that mean
    /// something.
    /// </summary>
    /// <remarks>
    /// Every remark in this repository is wrapped at the source file's width, and those breaks are an artefact
    /// of the file rather than of the sentence. Markdown would render them as spaces anyway; joining them here
    /// keeps the generated files readable by a person, which is the only reason they are checked in.
    /// <para>
    /// A fenced block and a list keep their own line breaks; a paragraph does not have any to keep.
    /// </para>
    /// </remarks>
    private static string Tidied(string text)
    {
        var blocks = Breaks().Split(text.Replace("\r\n", "\n", StringComparison.Ordinal));
        var kept = new List<string>();

        foreach (var block in blocks)
        {
            var body = block.Trim('\n');

            if (body.Trim().Length == 0)
                continue;

            kept.Add(body.Contains("```", StringComparison.Ordinal) || body.TrimStart().StartsWith("- ", StringComparison.Ordinal)
                ? body.Trim()
                : Spaces().Replace(body, " ").Trim());
        }

        return string.Join("\n\n", kept);
    }

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex Breaks();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
