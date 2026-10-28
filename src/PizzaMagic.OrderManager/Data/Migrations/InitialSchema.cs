using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PizzaMagic.OrderManager.Data.Migrations;

/// <summary>
/// Creates the full unified schema (all 33 tables, plan section 4.3) by running the hand-written SQL verbatim.
/// Migrations are hand-written SQL: never generate them with "dotnet ef migrations add" (this context maps
/// only some tables, so a generated diff would be wrong). Schema only: data comes from data/seed scripts.
/// </summary>
[DbContext(typeof(PizzaMagicDbContext))]
[Migration("20261028000000_InitialSchema")]
public sealed class InitialSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(SchemaScripts.Read("InitialSchema.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("The initial schema is not reversible; delete the database file instead.");
}

internal static class SchemaScripts
{
    public static string Read(string name)
    {
        var resource = $"PizzaMagic.OrderManager.Data.Schema.{name}";
        using var stream = typeof(SchemaScripts).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded schema script {resource} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
