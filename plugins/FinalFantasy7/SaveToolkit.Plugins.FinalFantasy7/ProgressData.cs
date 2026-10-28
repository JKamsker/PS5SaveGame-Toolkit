using System.Text.Json;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed record ProgressFlag(string Id, string Name, string Region, string Category, int Offset, int Bit, string Source);

/// <summary>Individual source candidates; shared placeholder addresses are excluded at extraction.</summary>
internal sealed class ProgressData(byte[] bytes, Ff7Schema schema, Func<ProgressFlag, FieldNode> bind)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly IReadOnlyList<ProgressFlag> Remake = Load("remake");
    private static readonly IReadOnlyList<ProgressFlag> Rebirth = Load("rebirth");
    public IReadOnlyList<ProgressFlag> Flags => schema.IsRemake ? Remake : Rebirth;

    public FieldNode Field(ProgressFlag flag) => bind(flag);

    public int Value(ProgressFlag flag) => (bytes[flag.Offset] >> flag.Bit) & 1;

    public CollectionQuery Collection() => new(new("progress", "Progress and regional candidates",
        [new("name", "Objective"), new("region", "Region"), new("category", "Domain / state"),
         new("value", "Stored bit", IntegerSpec.UInt8), new("address", "Byte / bit"), new("confidence", "Confidence"),
         new("source", "Source")], Flags.Count)
    {
        Description = "Inferred public-template bit edits. " + WriteWarnings.Progress + " " +
            "Repeated upstream placeholders and Intel Entries are excluded. " + EmbeddedCatalogs.Attribution,
    }, Entries);

    private IEnumerable<CollectionEntry> Entries() => Flags.Select(flag =>
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = flag.Name, ["region"] = flag.Region, ["category"] = flag.Category,
            ["value"] = ((bytes[flag.Offset] >> flag.Bit) & 1).ToString(),
            ["address"] = $"0x{flag.Offset:X} bit {flag.Bit}", ["confidence"] = "Inferred", ["source"] = flag.Source,
        };
        return new CollectionEntry($"progress/{flag.Id}", values, () =>
        {
            var cells = values.ToDictionary(pair => pair.Key, pair => (ViewNode)new TextNode(pair.Value), StringComparer.Ordinal);
            cells["value"] = bind(flag);
            return new($"progress/{flag.Id}", cells);
        });
    });

    private static IReadOnlyList<ProgressFlag> Load(string game)
    {
        using var stream = typeof(ProgressData).Assembly.GetManifestResourceStream(
            $"SaveToolkit.Plugins.FinalFantasy7.Resources.{game}-progress.json")
            ?? throw new InvalidOperationException("Embedded FFVII progress candidates are missing.");
        return JsonSerializer.Deserialize<ProgressFlag[]>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Embedded FFVII progress candidates are invalid.");
    }
}
