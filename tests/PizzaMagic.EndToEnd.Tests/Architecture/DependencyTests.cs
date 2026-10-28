using System.Reflection;
using System.Xml.Linq;
using PizzaMagic.Contracts;
using PizzaMagic.Domain.Monetary;
using PizzaMagic.OrderManager;
using PizzaMagic.Web;

namespace PizzaMagic.EndToEnd.Tests.Architecture;

/// <summary>
/// The dependency rules of plan section 1.2, checked two ways: the compiled assemblies' references (what the code
/// actually uses) and the project files (what it could use).
/// </summary>
public class DependencyTests
{
    private static readonly Assembly Domain = typeof(Money).Assembly;
    private static readonly Assembly Contracts = typeof(MoneyFormatter).Assembly;
    private static readonly Assembly OrderManager = typeof(OrderManagerMarker).Assembly;
    private static readonly Assembly Web = typeof(WebMarker).Assembly;

    private static IReadOnlyList<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

    private static bool IsFramework(string name) =>
        name is "netstandard" or "mscorlib" || name.StartsWith("System", StringComparison.Ordinal);

    /// <summary>The project's ProjectReference and PackageReference names.</summary>
    private static (IReadOnlyList<string> Projects, IReadOnlyList<string> Packages) ProjectFile(string project)
    {
        var xml = XDocument.Load(Path.Combine(Repository.Root, "src", project, $"{project}.csproj"));
        var projects = xml.Descendants("ProjectReference").Select(r => Path.GetFileNameWithoutExtension((string)r.Attribute("Include")!)).ToList();
        var packages = xml.Descendants("PackageReference").Select(r => (string)r.Attribute("Include")!).ToList();
        return (projects, packages);
    }

    [Fact]
    public void Domain_references_nothing()
    {
        References(Domain).Where(name => !IsFramework(name)).ShouldBeEmpty();
        References(Contracts).Where(name => !IsFramework(name)).ShouldBeEmpty();
        ProjectFile("PizzaMagic.Domain").Projects.ShouldBeEmpty();
        ProjectFile("PizzaMagic.Domain").Packages.ShouldBeEmpty();
        ProjectFile("PizzaMagic.Contracts").Projects.ShouldBeEmpty();
        ProjectFile("PizzaMagic.Contracts").Packages.ShouldBeEmpty();
    }

    // The order builder prices nothing and never touches the database: it knows only the wire contract.
    [Fact]
    public void Web_does_not_reference_Domain_or_EF()
    {
        var forbidden = References(Web).Where(name =>
            name == "PizzaMagic.Domain" || name == "PizzaMagic.OrderManager"
            || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal)
            || name.StartsWith("SQLitePCL", StringComparison.Ordinal));
        forbidden.ShouldBeEmpty();
        References(Web).ShouldContain("PizzaMagic.Contracts");

        var (projects, packages) = ProjectFile("PizzaMagic.Web");
        projects.ShouldBe(["PizzaMagic.Contracts"]);
        packages.ShouldNotContain(p => p.Contains("EntityFramework") || p.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OrderManager_references_Domain_and_Contracts()
    {
        References(OrderManager).ShouldContain("PizzaMagic.Domain");
        References(OrderManager).ShouldContain("PizzaMagic.Contracts");
        References(OrderManager).ShouldNotContain("PizzaMagic.Web");
        ProjectFile("PizzaMagic.OrderManager").Projects.OrderBy(p => p).ShouldBe(["PizzaMagic.Contracts", "PizzaMagic.Domain"]);
    }

    // Time zones come only from the seeded offset periods (UQ15): no runtime time zone library or OS data.
    [Fact]
    public void No_runtime_time_zone_library_in_src()
    {
        foreach (var assembly in new[] { Domain, Contracts, OrderManager, Web })
        {
            References(assembly).ShouldNotContain(name => name.StartsWith("NodaTime", StringComparison.Ordinal));
        }
        foreach (var project in new[] { "PizzaMagic.Domain", "PizzaMagic.Contracts", "PizzaMagic.OrderManager", "PizzaMagic.Web" })
        {
            ProjectFile(project).Projects.ShouldNotContain("PizzaMagic.TzSeedGen");
            ProjectFile(project).Packages.ShouldNotContain("NodaTime");
        }
        SourceScan.Files("src", "*.cs")
            .Where(f => SourceScan.Code(File.ReadAllText(f)).Contains("TimeZoneInfo", StringComparison.Ordinal))
            .Select(Repository.Relative)
            .ShouldBeEmpty();
    }
}
