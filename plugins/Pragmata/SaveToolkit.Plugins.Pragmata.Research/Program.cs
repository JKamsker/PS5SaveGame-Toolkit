using System.Globalization;
using System.Text.Json;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Pragmata.Research;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Main(string[] args)
    {
        try
        {
            if (args is ["index", var archive, var schemaPath, var output])
            {
                using var saves = SaveSetLoader.Load(archive);
                using var schema = new SchemaCatalog(schemaPath);
                var inventory = new EvidenceInventory(schema);
                foreach (var file in saves.Set.AllDiskFiles.Where(f => !f.Directory.IsBackup && new PragmataPlugin().Probe(f).IsMatch)
                    .DistinctBy(f => f.Sha256).OrderBy(f => f.FileName, StringComparer.Ordinal)) inventory.Add(file);
                Write(output, inventory.Build());
                return 0;
            }
            if (args is ["compare", var beforeArchive, var beforeFile, var beforeGeneration,
                var afterArchive, var afterFile, var afterGeneration, var reportPath])
            {
                // Detailed reports may contain user fields. Keep them outside repositories and artifacts.
                var full = Path.GetFullPath(reportPath);
                if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Comparison reports must be written underneath the system temporary directory.");
                using var before = SaveSetLoader.Load(beforeArchive);
                using var after = SaveSetLoader.Load(afterArchive);
                var b = Select(before.Set, beforeFile, beforeGeneration);
                var a = Select(after.Set, afterFile, afterGeneration);
                var decoder = new PragmataDecoder();
                var limits = new DecodeLimits { MaxValues = 1_000_000, MaxDepth = 100 };
                var bBytes = new DsssContainer(b.ReadAllBytes()).Plaintext;
                var aBytes = new DsssContainer(a.ReadAllBytes()).Plaintext;
                var semantic = SemanticDiffer.Compare(decoder.Decode(bBytes, new SaveLayout(bBytes).Roots, limits),
                    decoder.Decode(aBytes, new SaveLayout(aBytes).Roots, limits), maxChanges: 1000);
                var bytes = ByteDiffer.Compare(bBytes, aBytes);
                Write(full, new { format = "pragmata-diff/1", before = b.FileName, after = a.FileName,
                    warning = "Uncontrolled slot deltas establish variation only; no causal game semantics. Byte offsets are plaintext offsets. No churn is masked.",
                    bytes, semantic });
                Console.WriteLine($"Plaintext changed bytes: {bytes.ChangedBytes}; semantic changes: {semantic.Total}; input truncated: {semantic.InputTruncated}.");
                return semantic.Error is not null ? 2 : bytes.HasChanges || semantic.Total > 0 ? 1 : 0;
            }
            Console.Error.WriteLine("index <archive> <MIT-rsz.json> <manifest.json>\ncompare <before-archive> <file> <generation> <after-archive> <file> <generation> <temp-report.json>");
            return 2;
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or JsonException or Sdk.SaveFormatException)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
    }

    private static ISaveFile Select(Sdk.Saves.ISaveSet set, string fileName, string generation) => set.AllFiles.Single(f =>
        !f.Directory.IsBackup && f.FileName == fileName && f.Directory.Generation == int.Parse(generation, CultureInfo.InvariantCulture));

    private static void Write(string path, object value)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output must be a JSON file.");
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json) + "\n");
    }
}
