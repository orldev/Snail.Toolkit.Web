using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Snail.Toolkit.Web.Gen;

/// <summary>
/// Everything the compiler wrote down about the six packages, read back as one list.
/// </summary>
/// <remarks>
/// Two sources, and neither can replace the other. The prose comes from the generated <c>.xml</c>, which is
/// the only place the remarks exist outside the source. Whether a declaration belongs in a consumer's
/// reference comes from the ASSEMBLY, because the xml does not say: a member element carries a name and
/// nothing about accessibility, so a file listing <c>Markup</c> and <c>Remembered</c> beside <c>Link</c> and
/// <c>Outcome</c> looks exactly like a file listing only public types.
/// <para>
/// That is the whole reason this is a compiled tool rather than a script over the xml. Reading the xml alone
/// would publish, as a consumer's reference, the internal types this repository deliberately closed.
/// </para>
/// </remarks>
internal sealed partial class Catalogue
{
    /// <summary>The packages, in the order a reader meets them: the core first, then what stands on it.</summary>
    internal static readonly string[] Packages =
    [
        "Snail.Toolkit.Web",
        "Snail.Toolkit.Web.Search",
        "Snail.Toolkit.Web.Scraping",
        "Snail.Toolkit.Web.Search.Browser",
        "Snail.Toolkit.Web.Search.Browser.Agent",
        "Snail.Toolkit.Web.Scraping.Agent"
    ];

    internal required IReadOnlyList<Entry> Entries { get; init; }

    /// <summary>Reads the xml and the assemblies that sit beside this tool after a build.</summary>
    internal static Catalogue Read()
    {
        var beside = AppContext.BaseDirectory;
        var entries = new List<Entry>();

        foreach (var package in Packages)
        {
            var xml = Path.Combine(beside, $"{package}.xml");
            var dll = Path.Combine(beside, $"{package}.dll");

            if (!File.Exists(xml) || !File.Exists(dll))
                throw new FileNotFoundException(
                    $"'{package}' has no documentation beside the generator. Build the solution first: the "
                    + "xml is produced by the compiler and copied here by the project reference.");

            entries.AddRange(Echoes([.. Of(package, XDocument.Load(xml), Assembly.LoadFrom(dll))]));
        }

        return new Catalogue { Entries = entries };
    }

    private static IEnumerable<Entry> Of(string package, XDocument xml, Assembly assembly)
    {
        var exported = Exported(assembly);

        foreach (var member in xml.Descendants("member"))
        {
            if (member.Attribute("name")?.Value is not { Length: > 2 } id)
                continue;

            if (!IsOurs(id))
                continue;

            var summary = Prose.Of(member.Element("summary"));
            var remarks = Prose.Of(member.Element("remarks"));

            if (summary is null && remarks is null)
                continue;

            yield return new Entry
            {
                Id = id,
                Package = package,
                Kind = id[0],
                Name = Named(id),
                Owner = Owning(id),
                IsExported = IsExported(id, exported),
                Summary = summary,
                Remarks = remarks,
                Measured = [.. Dated().Matches(remarks ?? string.Empty).Select(found => found.Value).Distinct()]
            };
        }
    }

    /// <summary>Whether this declaration is the library's own rather than a generator's.</summary>
    /// <remarks>
    /// SOURCE GENERATORS WRITE INTO THE SAME XML, and the compiler documents what they emit. This library uses
    /// <c>[GeneratedRegex]</c> in four places, and each one puts a <c>RunnerFactory</c> and a <c>Runner</c>
    /// under <c>System.Text.RegularExpressions.Generated</c> into the file — types nobody wrote, with names
    /// that repeat once per pattern. The first version of this tool crashed on exactly that: two types called
    /// <c>RunnerFactory</c> in one package.
    /// <para>
    /// Filtering on OUR OWN root rather than on the regex generator's namespace is the point. The next
    /// generator added to this repository will emit something else under a name nobody predicted, and it will
    /// be excluded for the same reason this one is: the documentation of this library is what its authors
    /// wrote.
    /// </para>
    /// </remarks>
    private static bool IsOurs(string id) => id[2..].StartsWith("Snail.Toolkit.Web", StringComparison.Ordinal);

    /// <summary>The public types of a package, keyed by every spelling an xml id might use for them.</summary>
    /// <remarks>
    /// Nested types are the reason there are two keys. The xml writes <c>Outer.Inner</c> and reflection writes
    /// <c>Outer+Inner</c>, so a lookup on one spelling silently misses every nested type — and this library
    /// has several, all of them private, which would then be reported as "not public" for the right answer by
    /// the wrong route.
    /// </remarks>
    private static Dictionary<string, Type> Exported(Assembly assembly)
    {
        var exported = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.FullName is not { } name)
                continue;

            exported[name] = type;
            exported[name.Replace('+', '.')] = type;
        }

        return exported;
    }

    /// <summary>Whether a consumer of the package can see this declaration.</summary>
    /// <remarks>
    /// A type is public when the assembly exports it. A MEMBER is public when its declaring type is exported
    /// AND the member is among that type's public members — the second half matters, because a private helper
    /// on a public class is documented in the same xml as the class itself, and taking the type's word for it
    /// would put <c>Attempts.IsWorthRepeating</c> and <c>HostPoliteness.DelayAsync</c> in a consumer's
    /// reference.
    /// </remarks>
    private static bool IsExported(string id, Dictionary<string, Type> exported)
    {
        var body = id[2..];

        if (id[0] is 'T')
            return exported.ContainsKey(body);

        if (id[0] is 'N')
            return false;

        var withoutArguments = body.Split('(')[0];
        var cut = withoutArguments.LastIndexOf('.');

        if (cut < 0)
            return false;

        var owner = withoutArguments[..cut];
        var name = withoutArguments[(cut + 1)..].Split('`')[0];

        // An explicit interface implementation is written IFoo#Bar and is public through the interface.
        name = name.Split('#')[^1];

        return exported.TryGetValue(owner, out var type)
               && type.GetMember(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length > 0;
    }

    /// <summary>The bare name a heading shows: the type, or the member without its parameters.</summary>
    private static string Named(string id)
    {
        var body = id[2..].Split('(')[0];
        var cut = body.LastIndexOf('.');
        var name = (cut < 0 ? body : body[(cut + 1)..]).Split('`')[0];

        return name switch
        {
            "#ctor" => "constructor",
            "#cctor" => "static constructor",
            _ => name.Split('#')[^1]
        };
    }

    /// <summary>The type a member belongs to, or the type itself for a type.</summary>
    private static string Owning(string id)
    {
        var body = id[2..].Split('(')[0];

        if (id[0] is 'T')
            return body.Split('.')[^1].Split('`')[0];

        var cut = body.LastIndexOf('.');

        if (cut < 0)
            return body;

        // C# 14 lowers an extension block into a generated container, and the compiler writes THAT name into
        // the xml id: ServiceCollectionExtensions.<G>$7B30...AddSearchClient. The reader wants the class they
        // can see, so a segment that could not have been typed is stepped over. The same artefact makes
        // roslyn's find_unused_symbols report every one of these packages' registration classes as unused.
        var segments = body[..cut].Split('.');
        var at = segments.Length - 1;

        while (at > 0 && IsGenerated(segments[at]))
            at--;

        return segments[at].Split('`')[0];
    }

    private static bool IsGenerated(string segment)
        => segment.StartsWith('<') || segment.Contains('$', StringComparison.Ordinal);

    /// <summary>Drops the copies the compiler makes, which are the same prose twice under two ids.</summary>
    /// <remarks>
    /// A type with a primary constructor is documented once in the source and TWICE in the xml: the compiler
    /// copies the type's summary and remarks onto the constructor it generates. Left alone that is every
    /// record in the library appearing as <c>Effect</c> and again as <c>Effect.constructor</c>, with
    /// identical text — and, worse, counted twice, which would put the guard on these pages on a number that
    /// does not mean what it says.
    /// <para>
    /// Matched on the prose rather than on the kind, so a constructor that documents something of its own
    /// survives. There are none today; there is no reason to lose one tomorrow.
    /// </para>
    /// </remarks>
    private static IEnumerable<Entry> Echoes(IReadOnlyList<Entry> entries)
    {
        // Grouped rather than keyed directly: two types may legitimately share a short name in one assembly,
        // and a tool that throws on that would be reporting its own dictionary rather than the library.
        var types = entries
            .Where(entry => entry.IsType)
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .ToDictionary(found => found.Key, found => found.First(), StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!entry.IsType
                && types.TryGetValue(entry.Owner, out var owner)
                && entry.Summary == owner.Summary
                && entry.Remarks == owner.Remarks)
                continue;

            yield return entry;
        }
    }

    [GeneratedRegex(@"\b20\d{2}-\d{2}-\d{2}\b")]
    private static partial Regex Dated();

    /// <summary>One documented declaration, with what the compiler knows and what the author wrote.</summary>
    internal sealed class Entry
    {
        internal required string Id { get; init; }

        internal required string Package { get; init; }

        /// <summary>The xml id's first letter: T for a type, M for a method, P, F, E for the rest.</summary>
        internal required char Kind { get; init; }

        internal required string Name { get; init; }

        internal required string Owner { get; init; }

        internal required bool IsExported { get; init; }

        internal required string? Summary { get; init; }

        internal required string? Remarks { get; init; }

        /// <summary>The dates the remarks name, which is what makes an entry a record of a measurement.</summary>
        internal required IReadOnlyList<string> Measured { get; init; }

        internal bool IsType => Kind is 'T';
    }
}
