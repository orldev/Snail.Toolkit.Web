using Snail.Toolkit.Web.Gen;

// The documentation generator. Two modes and nothing else:
//
//   dotnet run --project docs/generator -- docs   write docs/content/reference and docs/content/decisions
//   dotnet run --project docs/generator -- check  fail if what is on disk is not what would be written
//
// The second is what CI runs, and it is the whole drift guard. A public type added without regenerating, or
// a generated page edited by hand, both end as a difference between the disk and the compiler's own output,
// and that difference is the only automatic signal there is: a page that is MISSING looks exactly like a page
// that is correct to every other check in this repository.

var mode = args.Length > 0 ? args[0] : string.Empty;

if (mode is not ("docs" or "check"))
{
    Console.WriteLine("The documentation generator:");
    Console.WriteLine("  docs    write docs/content/reference and docs/content/decisions from the xml and the assemblies");
    Console.WriteLine("  check   fail if what is on disk is not what would be written");
    Console.WriteLine();
    Console.WriteLine("  dotnet run --project docs/generator -- <mode>");

    return 1;
}

var root = Root();
var pages = Pages.Of(Catalogue.Read());

if (mode is "check")
    return Checked(root, pages);

foreach (var (where, body) in pages)
{
    var path = Path.Combine(root, "docs", "content", where);

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, body);
}

// Anything left over is a page whose package or whose reason to exist is gone; leaving it would publish a
// section that nothing generates any more.
Swept(root, pages);

Console.WriteLine($"{DateTime.Now:HH:mm} — pages written: {pages.Count}");

foreach (var (where, body) in pages.OrderBy(page => page.Key, StringComparer.Ordinal))
    Console.WriteLine($"  {where,-40} {body.Length,8:N0} characters");

return 0;

static int Checked(string root, IReadOnlyDictionary<string, string> pages)
{
    var wrong = new List<string>();

    foreach (var (where, body) in pages)
    {
        var path = Path.Combine(root, "docs", "content", where);

        if (!File.Exists(path))
            wrong.Add($"  MISSING      {where}");
        else if (File.ReadAllText(path) != body)
            wrong.Add($"  DIFFERS      {where}");
    }

    foreach (var extra in Extra(root, pages))
        wrong.Add($"  EXTRA        {extra}");

    if (wrong.Count == 0)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm} — the documentation matches the code, pages: {pages.Count}");

        return 0;
    }

    Console.WriteLine("The documentation no longer matches the code:");
    wrong.ForEach(Console.WriteLine);
    Console.WriteLine();
    Console.WriteLine("  Regenerate: dotnet run --project docs/generator -- docs");

    return 1;
}

static void Swept(string root, IReadOnlyDictionary<string, string> pages)
{
    foreach (var extra in Extra(root, pages))
        File.Delete(Path.Combine(root, "docs", "content", extra));
}

static IEnumerable<string> Extra(string root, IReadOnlyDictionary<string, string> pages)
{
    foreach (var section in (string[])["reference", "decisions"])
    {
        var folder = Path.Combine(root, "docs", "content", section);

        if (!Directory.Exists(folder))
            continue;

        foreach (var file in Directory.EnumerateFiles(folder, "*.md"))
        {
            var where = $"{section}/{Path.GetFileName(file)}";

            if (!pages.ContainsKey(where))
                yield return where;
        }
    }
}

// The tool runs from its own output directory, so the repository is found by walking up to the file that
// only its root has. Taking the current directory instead would work from a shell and fail from an IDE.
//
// THE ANCHOR IS THE SOLUTION FILE, AND IT USED TO BE mkdocs.yml — which broke the moment the documentation
// was gathered into docs/, because the anchor was a file the reorganisation itself moved. A marker for the
// root has to be something that is at the root BY DEFINITION rather than by today's arrangement: the
// solution is that, and it is also what would have to be renamed for this search to be wrong.
static string Root()
{
    var walking = new DirectoryInfo(AppContext.BaseDirectory);

    while (walking is not null)
    {
        if (walking.EnumerateFiles("*.slnx").Any())
            return walking.FullName;

        walking = walking.Parent;
    }

    throw new DirectoryNotFoundException(
        "The repository root was not found above the generator: no .slnx on the way up.");
}
