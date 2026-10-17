namespace Snail.Toolkit.Web.Probes;

/// <summary>
/// Reads the pages the probes build their measurements from, out of <c>Pages/</c> beside the program.
/// </summary>
/// <remarks>
/// The pages are data in the language they were measured in. Kept as files rather than as literals, their text
/// is input to the measurement, not a message of the program, and changing it is a change of what is measured.
/// </remarks>
internal static class Samples
{
    /// <summary>The whole page, without the line end the file closes with.</summary>
    public static string Page(string name) => File.ReadAllText(PathOf(name)).TrimEnd('\r', '\n');

    /// <summary>The page's lines, each without its line end.</summary>
    public static string[] Lines(string name) => File.ReadAllLines(PathOf(name));

    private static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Pages", name);
}
