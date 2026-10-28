using PizzaMagic.TzSeedGen;

// Usage: dotnet run --project tools/PizzaMagic.TzSeedGen -- [output path]
// Default output: data/seed/0000-time-zones.sql relative to the current directory (run from the repository root).
var output = args.Length > 0 ? args[0] : Path.Combine("data", "seed", "0000-time-zones.sql");
File.WriteAllText(output, TimeZoneSeedGenerator.Generate());
Console.WriteLine($"Wrote {output}");
