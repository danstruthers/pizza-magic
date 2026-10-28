using System.Text.RegularExpressions;

namespace PizzaMagic.EndToEnd.Tests.Architecture;

/// <summary>
/// Single source of truth and explicit rounding (plan section 9.2, items 2 and 3), checked on the source text with
/// comments and strings removed.
/// </summary>
public partial class RoundingScanTests
{
    private static readonly string[] EngineFolders = ["Catalog", "Pricing", "Promotions", "Tax"];

    /// <summary>Where Truncate may appear: its two definitions and the one caller (the TRUNCATE tax method).</summary>
    private static readonly string[] TruncateAllowed =
    [
        "src/PizzaMagic.Domain/Monetary/Rounding.cs",
        "src/PizzaMagic.Domain/Monetary/Money.cs",
        "src/PizzaMagic.Domain/Tax/TaxCalculator.cs",
    ];

    private static IEnumerable<(string File, int Line, string Text)> CodeLines(IEnumerable<string> files) =>
        files.SelectMany(file => SourceScan.Code(File.ReadAllText(file)).Split('\n')
            .Select((text, index) => (Repository.Relative(file), index + 1, text)));

    private static IEnumerable<string> EngineFiles() =>
        EngineFolders.SelectMany(folder => SourceScan.Files(Path.Combine("src", "PizzaMagic.Domain", folder), "*.cs"));

    // Every number in a price comes from the catalog. The engine may count (0, 1, 2) and name its constants
    // (PpmScale, MaxJurisdictionDepth, ...), and it never uses floating point or library rounding.
    [Fact]
    public void No_pricing_literals_or_rounding_in_engine_sources()
    {
        EngineFiles().ShouldNotBeEmpty();
        var findings = new List<string>();
        foreach (var (file, line, text) in CodeLines(EngineFiles()))
        {
            foreach (Match forbidden in ForbiddenToken().Matches(text))
            {
                findings.Add($"{file}:{line}: {forbidden.Value}");
            }
            if (ConstDeclaration().IsMatch(text))
            {
                continue;
            }
            foreach (Match literal in NumericLiteral().Matches(text))
            {
                if (literal.Value is not ("0" or "1" or "2"))
                {
                    findings.Add($"{file}:{line}: literal {literal.Value}");
                }
            }
        }
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Only_TaxCalculator_references_Truncate()
    {
        var findings = CodeLines(SourceScan.Files("src", "*.cs"))
            .Where(l => TruncateCall().IsMatch(l.Text) && !TruncateAllowed.Contains(l.File))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}")
            .ToList();
        findings.ShouldBeEmpty();

        // And the one caller really does call it (the scan would pass vacuously after a rename).
        CodeLines([Path.Combine(Repository.Root, "src", "PizzaMagic.Domain", "Tax", "TaxCalculator.cs")])
            .ShouldContain(l => TruncateCall().IsMatch(l.Text));
    }

    [Fact]
    public void No_library_rounding_anywhere_in_src()
    {
        CodeLines(SourceScan.Files("src", "*.cs"))
            .Where(l => LibraryRounding().IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}")
            .ShouldBeEmpty();
    }

    [Fact]
    public void Scanner_ignores_comments_and_strings_but_not_code()
    {
        var code = SourceScan.Code(""""
            var a = 3; // 45 in a comment
            var s = "Math.Round(7)"; var t = @"say ""99""" ; var c = '9';
            /* 1234
               5678 */ var b = Math.Round(x);
            """");
        NumericLiteral().Matches(code).Select(m => m.Value).ShouldBe(["3"]);
        LibraryRounding().Matches(code).Count.ShouldBe(1);
        code.Split('\n').Length.ShouldBe(4);   // line numbers survive
    }

    [GeneratedRegex(@"\b(double|float|decimal)\b|\bMath\.(Round|Floor|Truncate|Ceiling)\b|\bMidpointRounding\b")]
    private static partial Regex ForbiddenToken();

    [GeneratedRegex(@"\bconst\b")]
    private static partial Regex ConstDeclaration();

    [GeneratedRegex(@"(?<![\w.])\d[\d_]*(\.\d+)?([eE][+-]?\d+)?[uUlLmMdDfF]*(?!\w)")]
    private static partial Regex NumericLiteral();

    [GeneratedRegex(@"\bRounding\.Truncate\b|\bApplyRateTruncated\b|(?<![\w.])Truncate\s*\(")]
    private static partial Regex TruncateCall();

    [GeneratedRegex(@"\bMath\.Round\b|\bMidpointRounding\b|\bdecimal\.Round\b|\bMath\.Truncate\b")]
    private static partial Regex LibraryRounding();
}
