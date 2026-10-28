using System.Text;

namespace PizzaMagic.EndToEnd.Tests.Architecture;

/// <summary>The repository root (the folder holding PizzaMagic.sln), found from the test's output folder.</summary>
public static class Repository
{
    public static readonly string Root = Find();

    public static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PizzaMagic.sln")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("PizzaMagic.sln not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// A small C# reader for source-scan tests: it blanks out comments, strings and character literals (keeping line
/// breaks, so line numbers stay right), leaving only code. Interpolation holes are blanked with their string, which
/// can only hide code from a scan, never invent a match.
/// </summary>
public static class SourceScan
{
    /// <summary>Source files under a repository folder, excluding build output and migrations' designer files.</summary>
    public static IEnumerable<string> Files(string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Repository.Root, folder), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(f => f, StringComparer.Ordinal);

    public static string Code(string source)
    {
        var code = new StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
            }
            else if (c == '/' && next == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                Blank(code, source, i, end);
                i = end;
            }
            else if (c == '"' && source.AsSpan(i).StartsWith("\"\"\""))
            {
                var end = source.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 3;
                Blank(code, source, i, end);
                i = end;
            }
            else if (c == '"' || (c is '@' or '$' && next == '"') || (c is '@' or '$' && next is '@' or '$' && i + 2 < source.Length && source[i + 2] == '"'))
            {
                var verbatim = source.AsSpan(i, Math.Min(3, source.Length - i)).Contains('@');
                var start = i;
                i = source.IndexOf('"', i) + 1;
                while (i < source.Length)
                {
                    if (verbatim && source[i] == '"' && i + 1 < source.Length && source[i + 1] == '"') { i += 2; continue; }
                    if (!verbatim && source[i] == '\\') { i += 2; continue; }
                    if (source[i] == '"') { i++; break; }
                    if (!verbatim && source[i] == '\n') break;
                    i++;
                }
                Blank(code, source, start, i);
            }
            else if (c == '\'')
            {
                var start = i++;
                while (i < source.Length && source[i] != '\'' && source[i] != '\n')
                {
                    i += source[i] == '\\' ? 2 : 1;
                }
                i++;
                Blank(code, source, start, Math.Min(i, source.Length));
            }
            else
            {
                code.Append(c);
                i++;
            }
        }
        return code.ToString();
    }

    private static void Blank(StringBuilder code, string source, int start, int end)
    {
        for (var j = start; j < end; j++)
        {
            code.Append(source[j] == '\n' ? '\n' : ' ');
        }
    }
}
