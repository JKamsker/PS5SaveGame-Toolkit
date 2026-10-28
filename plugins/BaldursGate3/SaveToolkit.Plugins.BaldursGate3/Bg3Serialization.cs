using System.Globalization;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3;

internal static class Bg3Serialization
{
    internal static IReadOnlyList<FileOutput> Write(string path, byte[] original, LspkPackage? package,
        IReadOnlyList<DocumentEntry> entries, IReadOnlyDictionary<string, byte[]> edits, PartySummaryEdits? summary,
        LsmfLiveWorkspace live)
    {
        var changed = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (summary?.HasEdits == true) changed.Add(summary.EntryName, summary.Write());
        for (var entry = 0; entry < entries.Count; entry++)
        {
            var prefix = $"resource/{entry}/attribute/";
            var replacements = edits.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(pair => int.Parse(pair.Key.AsSpan(prefix.Length), CultureInfo.InvariantCulture), pair => pair.Value);
            foreach (var (attribute, bytes) in live.Replacements(entry))
                BinaryData.Require(replacements.TryAdd(attribute, bytes), "Conflicting raw and NewAge edits.");
            if (replacements.Count > 0) changed.Add(entries[entry].Name, LsfWriter.Write(entries[entry].Bytes, replacements));
        }
        if (changed.Count == 0) return [new FileOutput(path, original.ToArray())];
        var output = package is null ? changed[entries[0].Name] : LspkWriter.Write(original, changed);
        var budget = new ResourceBudget();
        if (package is null) _ = LsfReader.Read(output, budget);
        else
        {
            var reopened = LspkPackage.Read(output);
            foreach (var entry in reopened.Entries) _ = ReadResource(entry.Name, reopened.Extract(entry), budget);
        }
        return [new FileOutput(path, output)];
    }

    internal static LarianResource? ReadResource(string name, byte[] bytes, ResourceBudget budget) =>
        name.EndsWith(".lsf", StringComparison.OrdinalIgnoreCase) ? LsfReader.Read(bytes, budget) :
        name.EndsWith(".lsx", StringComparison.OrdinalIgnoreCase) ? LsxReader.Read(bytes, budget) : null;
}
