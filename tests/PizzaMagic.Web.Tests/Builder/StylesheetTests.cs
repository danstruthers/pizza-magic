using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PizzaMagic.Web.Tests.Builder;

/// <summary>
/// The app ships Bootstrap 5.1 (wwwroot/lib), where an undefined color class renders nothing: badges built with
/// Bootstrap 5.2's <c>text-bg-*</c> showed white text on no background, invisible to text-based tests. Every color
/// class the app uses must exist in the shipped stylesheet.
/// </summary>
public partial class StylesheetTests
{
    private static string WebProject([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "src", "PizzaMagic.Web"));

    [Fact]
    public void Every_colour_class_used_exists_in_the_shipped_bootstrap()
    {
        var web = WebProject();
        var css = File.ReadAllText(Path.Combine(web, "wwwroot", "lib", "bootstrap", "dist", "css", "bootstrap.min.css"));
        var sources = Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cshtml") || f.EndsWith(".cs") || f.EndsWith(".js") || f.EndsWith(".mjs"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        sources.ShouldNotBeEmpty();

        var used = sources
            .SelectMany(f => ColourClass().Matches(File.ReadAllText(f)).Select(m => (Class: m.Value, File: Path.GetFileName(f))))
            .Distinct()
            .ToList();
        used.ShouldNotBeEmpty();

        used.Where(u => !css.Contains("." + u.Class + "{") && !css.Contains("." + u.Class + ","))
            .Select(u => $"{u.Class} ({u.File})")
            .ShouldBeEmpty();
    }

    [GeneratedRegex(@"(?<![\w-])(?:text-bg|bg|text)-(?:primary|secondary|success|danger|warning|info|light|dark|white|muted|body)(?![\w-])")]
    private static partial Regex ColourClass();
}
