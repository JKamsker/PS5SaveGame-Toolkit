using System.Text.Json;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsEvidenceTests
{
    [Fact]
    public void AvailableSlotSnapshots_HaveReproducibleF1TableAndRowDifferences()
    {
        var samples = SampleSaves.Load("PPSA01603").AllDiskFiles.Where(file =>
            !file.Directory.IsBackup && file.FileName == "ue4savegame.ps5.sav" && file.Directory.Name.StartsWith("HL00", StringComparison.Ordinal))
            .OrderBy(file => file.Directory.Generation).ThenBy(file => file.Directory.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(15, samples.Length);
        var decoder = new HogwartsSaveDecoder();
        var previous = decoder.Decode(samples[0], new());
        var summaries = new List<object>();
        var changesByTable = new Dictionary<string, int>(StringComparer.Ordinal);
        Assert.False(previous.Truncated);
        for (int i = 1; i < samples.Length; i++)
        {
            Assert.True(decoder.CanDecode(samples[i]));
            var current = decoder.Decode(samples[i], new());
            var diff = SemanticDiffer.Compare(previous, current, maxChanges: int.MaxValue);
            Assert.False(diff.InputTruncated);
            Assert.True(diff.Total > 0);
            if (samples[i - 1].Directory.Name == "HL0010" && samples[i].Directory.Name == "HL0011")
                Assert.Equal((6, 2, 24), (diff.Added, diff.Removed, diff.Changed));
            foreach (var change in diff.Changes.Where(change => change.Path.Contains("/rows/", StringComparison.Ordinal)))
            {
                string table = change.Path.Split('/')[2];
                changesByTable[table] = changesByTable.GetValueOrDefault(table) + 1;
            }
            summaries.Add(new
            {
                Before = samples[i - 1].Directory.Name, After = samples[i].Directory.Name,
                diff.Added, diff.Removed, diff.Changed, diff.BeforeValues, diff.AfterValues,
                IdentityWarnings = current.Warnings.Count,
            });
            previous = current;
        }
        Assert.True(changesByTable.GetValueOrDefault("InventoryDynamic") > 0);
        Assert.True(changesByTable.GetValueOrDefault("CollectionDynamic") > 0);
        // Only counts and public schema names, never row contents, names, account identifiers or save bytes.
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "hogwarts-f1-summary.json"), JsonSerializer.Serialize(new
        {
            Caveat = "Uncontrolled slot snapshots, ordered by slot name; no event causality or chronology claimed.",
            Generations = samples.Select(file => file.Directory.Generation).Distinct(), Pairs = summaries, ChangesByTable = changesByTable,
        }));
        Assert.True(decoder.Decode(samples[0], new() { MaxValues = 1 }).Truncated);
    }
}
