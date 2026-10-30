using SaveToolkit.Testing.UnrealBrowser;

if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]))
    throw new ArgumentException("Supply one absolute, process-owned scratch output directory.");

Directory.CreateDirectory(args[0]);
foreach (var (name, bytes) in UnrealBrowserFixtures.Files())
    File.WriteAllBytes(Path.Combine(args[0], name), bytes);
