using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed record EnemyReference(int Index, string Name, string Category);

/// <summary>Read-only source-indexed enemy records; display numbers never become record offsets.</summary>
internal sealed class EnemyIntel(byte[] bytes, Ff7Schema schema)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly IReadOnlyList<EnemyReference> Remake = Load("remake");
    private static readonly IReadOnlyList<EnemyReference> Rebirth = Load("rebirth");
    private IReadOnlyList<EnemyReference> Enemies => schema.IsRemake ? Remake : Rebirth;
    private int BaseOffset => schema.IsRemake ? 0x42FAC : 0x450D0;
    private int Stride => schema.IsRemake ? 8 : 16;

    public CollectionQuery Collection() => new(new("enemy-intel", "Enemy intel candidates",
        [new("name", "Enemy / source display number"), new("index", "Record index", IntegerSpec.UInt32), new("category", "Source group"),
         new("entry", "Entry bit", IntegerSpec.UInt8), new("assessed", "Assessed bit", IntegerSpec.UInt8),
         new("staggered", "Staggered bit", IntegerSpec.UInt8), new("defeated", "Defeated count", IntegerSpec.UInt32),
         new("address", "Record byte"), new("source", "Source / confidence")], Enemies.Count)
    {
        Description = "Read-only/Inferred public-template enemy records. Source numeric indices select records; display numbers are labels only. Unknown flags and padding are preserved. " + EmbeddedCatalogs.Attribution,
    }, () => Enemies.Select(enemy =>
    {
        var offset = BaseOffset + enemy.Index * Stride;
        var defeated = schema.IsRemake ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + sizeof(uint)))
            : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + sizeof(uint)));
        return new CollectionEntry($"enemy/{enemy.Index:X}", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = enemy.Name, ["index"] = enemy.Index.ToString(CultureInfo.InvariantCulture), ["category"] = enemy.Category,
            ["entry"] = Bit(0), ["assessed"] = Bit(1), ["staggered"] = Bit(5),
            ["defeated"] = defeated.ToString(CultureInfo.InvariantCulture), ["address"] = $"0x{offset:X}",
            ["source"] = "Inferred: pinned template.ts and resource.ts enemy index/name tables. " + EmbeddedCatalogs.Attribution,
        });

        string Bit(int bit) => ((bytes[offset] >> bit) & 1).ToString(CultureInfo.InvariantCulture);
    }));

    private static IReadOnlyList<EnemyReference> Load(string game)
    {
        using var stream = typeof(EnemyIntel).Assembly.GetManifestResourceStream(
            $"SaveToolkit.Plugins.FinalFantasy7.Resources.{game}-enemies.json")
            ?? throw new InvalidOperationException("Embedded FFVII enemy references are missing.");
        return JsonSerializer.Deserialize<EnemyReference[]>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Embedded FFVII enemy references are invalid.");
    }
}
