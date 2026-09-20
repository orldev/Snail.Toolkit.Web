using System.Text;

namespace Snail.Toolkit.Web.Gen;

/// <summary>
/// Writes the two generated sections, which answer two different questions for two different readers.
/// </summary>
/// <remarks>
/// THE REFERENCE is for somebody consuming a package: public declarations only, because a reference listing a
/// type the reader cannot name is worse than one that omits it — they will try.
/// <para>
/// THE DECISIONS are for somebody changing this library, and they include what is internal, because that is
/// where most of the reasoning lives: why the nesting ceiling is a constant rather than a setting is written
/// on <c>Markup</c>, which no consumer can see.
/// </para>
/// <para>
/// The two are not the same text with a different filter. An entry earns a place among the decisions by
/// NAMING A DATE — that is what separates a remark carrying a measurement from a remark explaining a
/// parameter, and it is the same rule this library holds its own comments to. It is also what makes the
/// section countable, which is what keeps it honest: the number of dated remarks in the xml and the number of
/// entries on these pages are the same number, and the generator refuses to finish when they are not.
/// </para>
/// </remarks>
internal static class Pages
{
    internal static IReadOnlyDictionary<string, string> Of(Catalogue catalogue)
    {
        var written = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var package in Catalogue.Packages)
        {
            written[$"reference/{Slug(package)}.md"] = Reference(catalogue, package);

            if (Dated(catalogue, package).Count > 0)
                written[$"decisions/{Slug(package)}.md"] = Decisions(catalogue, package);
        }

        written["reference/index.md"] = ReferenceIndex(catalogue);
        written["decisions/index.md"] = DecisionsIndex(catalogue);

        return written;
    }

    // ---------- the reference ----------

    private static string Reference(Catalogue catalogue, string package)
    {
        var page = new StringBuilder();
        var types = Types(catalogue, package);

        page.Append("# ").Append(package).Append("\n\n");
        page.Append(Generated());
        page.Append("The public surface of this package: ").Append(Counted(types.Count, "type"))
            .Append(". Everything else it contains is internal and is described under ")
            .Append("[Decisions](../decisions/index.md) where it carries a measurement.\n");

        foreach (var type in types.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            page.Append("\n## ").Append(type.Name).Append("\n\n");

            if (type.Summary is { } summary)
                page.Append(summary).Append("\n\n");

            if (type.Remarks is { } remarks)
                page.Append(remarks).Append("\n\n");

            foreach (var member in Members(catalogue, package, type.Name))
            {
                page.Append("**").Append(member.Name).Append("** — ")
                    .Append(member.Summary ?? "*no summary*").Append("\n\n");

                if (member.Remarks is { } said)
                    page.Append(said).Append("\n\n");
            }
        }

        return page.ToString().TrimEnd() + "\n";
    }

    private static string ReferenceIndex(Catalogue catalogue)
    {
        var page = new StringBuilder();

        page.Append("# Reference\n\n");
        page.Append(Generated());
        page.Append("Every public declaration in the six packages, with what its author wrote about it.\n\n");
        page.Append("| Package | Public types |\n| --- | --- |\n");

        foreach (var package in Catalogue.Packages)
            page.Append("| [").Append(package).Append("](").Append(Slug(package)).Append(".md) | ")
                .Append(Types(catalogue, package).Count).Append(" |\n");

        page.Append("\nThe count is the assembly's, not the file's: it comes from `GetExportedTypes()` rather ")
            .Append("than from what these pages happen to list, so a type added without regenerating makes ")
            .Append("the two disagree and the build stop.\n");

        return page.ToString();
    }

    // ---------- the decisions ----------

    private static string Decisions(Catalogue catalogue, string package)
    {
        var page = new StringBuilder();
        var dated = Dated(catalogue, package);

        page.Append("# ").Append(package).Append("\n\n");
        page.Append(Generated());
        page.Append(Counted(dated.Count, "decision")).Append(" recorded in this package, each naming the day ")
            .Append("it was measured. Declarations a consumer cannot see are marked *internal*: they are here ")
            .Append("because the reasoning is, not because the type is reachable.\n");

        foreach (var entry in dated)
        {
            page.Append("\n## ").Append(Titled(entry)).Append("\n\n");
            page.Append("`").Append(string.Join("`, `", entry.Measured)).Append('`');

            if (!entry.IsExported)
                page.Append(" · *internal*");

            page.Append("\n\n");

            if (entry.Summary is { } summary)
                page.Append('*').Append(summary.TrimEnd('.')).Append(".*\n\n");

            page.Append(entry.Remarks).Append("\n\n");
        }

        return page.ToString().TrimEnd() + "\n";
    }

    private static string DecisionsIndex(Catalogue catalogue)
    {
        var page = new StringBuilder();
        var all = catalogue.Entries.Count(entry => entry.Measured.Count > 0);

        page.Append("# Decisions\n\n");
        page.Append(Generated());
        page.Append("A remark in this library carries a measurement rather than a paraphrase, and a ")
            .Append("measurement carries the day it was taken. These pages are every one of them — ")
            .Append(Counted(all, "entry")).Append(" — gathered out of the source they annotate.\n\n");
        page.Append("They include internal declarations on purpose. The argument against making the nesting ")
            .Append("ceiling a setting is written on a type no consumer can name, and it is the argument that ")
            .Append("matters.\n\n");
        page.Append("| Package | Decisions |\n| --- | --- |\n");

        foreach (var package in Catalogue.Packages)
        {
            var dated = Dated(catalogue, package).Count;

            page.Append("| ");

            if (dated > 0)
                page.Append('[').Append(package).Append("](").Append(Slug(package)).Append(".md)");
            else
                page.Append(package);

            page.Append(" | ").Append(dated).Append(" |\n");
        }

        page.Append("\nAn entry that stops being true is a comment that can be proved wrong, which is the ")
            .Append("point of writing the number down. Nothing turns red when a measurement goes stale — that ")
            .Append("is the one thing here held up by discipline rather than by code.\n");

        return page.ToString();
    }

    // ---------- shared ----------

    private static IReadOnlyList<Catalogue.Entry> Types(Catalogue catalogue, string package)
        => [.. catalogue.Entries.Where(entry => entry.Package == package && entry.IsType && entry.IsExported)];

    private static IReadOnlyList<Catalogue.Entry> Members(Catalogue catalogue, string package, string owner)
        => [.. catalogue.Entries
            .Where(entry => entry.Package == package && !entry.IsType && entry.IsExported && entry.Owner == owner)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)];

    private static IReadOnlyList<Catalogue.Entry> Dated(Catalogue catalogue, string package)
        => [.. catalogue.Entries
            .Where(entry => entry.Package == package && entry.Measured.Count > 0)
            .OrderBy(entry => entry.Measured[0], StringComparer.Ordinal)
            .ThenBy(entry => entry.Owner, StringComparer.Ordinal)];

    private static string Titled(Catalogue.Entry entry)
        => entry.IsType ? entry.Name : $"{entry.Owner}.{entry.Name}";

    private static string Counted(int many, string thing)
        => many == 1 ? $"one {thing}" : $"{many} {Plural(thing)}";

    private static string Plural(string thing)
        => thing.EndsWith('y') ? $"{thing[..^1]}ies" : $"{thing}s";

    /// <summary>The line that stops somebody editing a generated file and losing the edit on the next build.</summary>
    private static string Generated()
        => "*Generated from the compiler's documentation by `dotnet run --project docs/generator -- docs`. "
           + "Editing this file by hand is lost on the next build; edit the remark in the source instead.*\n\n";

    private static string Slug(string package)
        => package.Replace("Snail.Toolkit.Web", string.Empty, StringComparison.Ordinal)
                  .Trim('.')
                  .Replace('.', '-')
                  .ToLowerInvariant() is { Length: > 0 } slug
            ? slug
            : "web";
}
