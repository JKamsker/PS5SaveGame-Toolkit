using System.Text.Json;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Read-only source candidates; a raw bit does not implement crafting or reward recipes.</summary>
internal sealed class ResearchProgress(byte[] bytes, Func<ScalarField, FieldNode> bind)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly IReadOnlyList<ProgressFlag> Flags = Load();

    public CollectionQuery Collection() => new(new("research-progress", "Crafting and minigame candidates",
        [new("name", "Candidate"), new("module", "Module"), new("category", "Domain / state"), new("bit", "Stored bit", IntegerSpec.UInt8),
         new("address", "Byte / bit"), new("raw", "Raw byte", IntegerSpec.UInt8), new("source", "Source")], Flags.Count)
    {
        Description = "Read-only/Inferred static source candidates. These bits do not establish recipes, ingredient costs, scores, rewards or actor state. " + EmbeddedCatalogs.Attribution,
    }, () => Flags.Select(flag =>
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = flag.Name, ["module"] = flag.Region, ["category"] = flag.Category,
            ["bit"] = ((bytes[flag.Offset] >> flag.Bit) & 1).ToString(), ["address"] = $"0x{flag.Offset:X} bit {flag.Bit}",
            ["raw"] = bytes[flag.Offset].ToString(),
            ["source"] = flag.Source + "; " + EmbeddedCatalogs.Attribution,
        };
        return new CollectionEntry(flag.Id, values, () =>
        {
            var cells = values.ToDictionary(pair => pair.Key, pair => (ViewNode)new TextNode(pair.Value), StringComparer.Ordinal);
            cells["raw"] = bind(new($"research/{flag.Offset:X}/raw", $"Raw byte 0x{flag.Offset:X}", flag.Offset,
                IntegerSpec.UInt8, Confidence.Inferred, Byte: true, Warning: flag.Source + "; " + EmbeddedCatalogs.Attribution +
                    " Read-only candidate; no complete crafting/purchase/reward writer exists."));
            return new(flag.Id, cells);
        });
    }));

    private static IReadOnlyList<ProgressFlag> Load()
    {
        using var stream = typeof(ResearchProgress).Assembly.GetManifestResourceStream(
            "SaveToolkit.Plugins.FinalFantasy7.Resources.rebirth-research.json")
            ?? throw new InvalidOperationException("Embedded FFVII research candidates are missing.");
        return JsonSerializer.Deserialize<ProgressFlag[]>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Embedded FFVII research candidates are invalid.");
    }
}
