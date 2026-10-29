using PizzaMagic.FantasySeedGen;

// Usage: dotnet run --project tools/PizzaMagic.FantasySeedGen -- [output path]
// Default output: data/test/fantasy-realm.sql relative to the current directory (run from the repository root).
var output = args.Length > 0 ? args[0] : Path.Combine("data", "test", "fantasy-realm.sql");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, FantasyRealmGenerator.Generate());
Console.WriteLine($"Wrote {output}");
