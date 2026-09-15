using Snail.Toolkit.Web.Gen;

// The documentation generator. Two modes and nothing else:
//
//   dotnet run --project gen -- docs      write docs/reference and docs/decisions
//   dotnet run --project gen -- check     fail if what is on disk is not what would be written
//
// The second is what CI runs, and it is the whole drift guard. A public type added without regenerating, or
// a generated page edited by hand, both end as a difference between the disk and the compiler's own output,
// and that difference is the only automatic signal there is: a page that is MISSING looks exactly like a page
// that is correct to every other check in this repository.

var mode = args.Length > 0 ? args[0] : string.Empty;

if (mode is not ("docs" or "check"))
{
    Console.WriteLine("Генератор документации:");
    Console.WriteLine("  docs    записать docs/reference и docs/decisions из xml и сборок");
    Console.WriteLine("  check   упасть, если на диске лежит не то, что сгенерировалось бы");
    Console.WriteLine();
    Console.WriteLine("  dotnet run --project gen -- <режим>");

    return 1;
}

var root = Root();
var pages = Pages.Of(Catalogue.Read());

if (mode is "check")
    return Checked(root, pages);

foreach (var (where, body) in pages)
{
    var path = Path.Combine(root, "docs", where);

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, body);
}

// Anything left over is a page whose package or whose reason to exist is gone; leaving it would publish a
// section that nothing generates any more.
Swept(root, pages);

Console.WriteLine($"{DateTime.Now:HH:mm} — записано страниц: {pages.Count}");

foreach (var (where, body) in pages.OrderBy(page => page.Key, StringComparer.Ordinal))
    Console.WriteLine($"  {where,-40} {body.Length,8:N0} символов");

return 0;

static int Checked(string root, IReadOnlyDictionary<string, string> pages)
{
    var wrong = new List<string>();

    foreach (var (where, body) in pages)
    {
        var path = Path.Combine(root, "docs", where);

        if (!File.Exists(path))
            wrong.Add($"  ОТСУТСТВУЕТ  {where}");
        else if (File.ReadAllText(path) != body)
            wrong.Add($"  РАСХОДИТСЯ   {where}");
    }

    foreach (var extra in Extra(root, pages))
        wrong.Add($"  ЛИШНЯЯ       {extra}");

    if (wrong.Count == 0)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm} — документация совпадает с кодом, страниц: {pages.Count}");

        return 0;
    }

    Console.WriteLine("Документация разошлась с кодом:");
    wrong.ForEach(Console.WriteLine);
    Console.WriteLine();
    Console.WriteLine("  Перегенерировать: dotnet run --project gen -- docs");

    return 1;
}

static void Swept(string root, IReadOnlyDictionary<string, string> pages)
{
    foreach (var extra in Extra(root, pages))
        File.Delete(Path.Combine(root, "docs", extra));
}

static IEnumerable<string> Extra(string root, IReadOnlyDictionary<string, string> pages)
{
    foreach (var section in (string[])["reference", "decisions"])
    {
        var folder = Path.Combine(root, "docs", section);

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
static string Root()
{
    var walking = new DirectoryInfo(AppContext.BaseDirectory);

    while (walking is not null)
    {
        if (File.Exists(Path.Combine(walking.FullName, "mkdocs.yml")))
            return walking.FullName;

        walking = walking.Parent;
    }

    throw new DirectoryNotFoundException(
        "The repository root was not found above the generator: no mkdocs.yml on the way up.");
}
