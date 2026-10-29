using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Immutable fallback over the very same records shipped in the host's F2 packs.</summary>
internal sealed class SerialDatabase
{
    private static readonly Lazy<SerialDatabase> Bl3 = new(() => new("bl3", 62));
    private static readonly Lazy<SerialDatabase> Tina = new(() => new("ttwl", 59));
    private readonly Dictionary<string, Lazy<Dictionary<string, ReferenceRecord>>> _catalogs = new(StringComparer.Ordinal);
    private readonly Lazy<Dictionary<string, (int Version, int Bits)[]>> _widths;

    public int MaximumVersion { get; }
    public ReferencePackInfo Pack { get; }
    public static SerialDatabase For(bool wonderlands) => (wonderlands ? Tina : Bl3).Value;

    internal SerialDatabase(string game, int maximumVersion)
    {
        MaximumVersion = maximumVersion;
        var prefix = $"refpacks/gearbox.{game}/";
        using var manifest = Read(prefix + "pack.json");
        var model = manifest.RootElement;
        Pack = new ReferencePackInfo(model.GetProperty("id").GetString()!, model.GetProperty("version").GetString()!,
            model.GetProperty("title").GetString()!, ReferencePackOrigin.Embedded);
        foreach (var dataset in model.GetProperty("datasets").EnumerateArray())
        {
            // F2 owns image validation/storage; the binary-layout fallback needs only records.
            if (dataset.GetProperty("kind").GetString() != "records") continue;
            var id = dataset.GetProperty("id").GetString()!;
            var a = dataset.GetProperty("attribution");
            var attribution = new ReferenceAttribution(a.GetProperty("sourceUrl").GetString()!, a.GetProperty("author").GetString()!,
                a.GetProperty("license").GetString()!, a.GetProperty("licenseUrl").GetString()!,
                DateOnly.ParseExact(a.GetProperty("retrieved").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                a.GetProperty("modifications").GetString()!);
            var info = new ReferenceDatasetInfo(Pack.Id, id, ReferenceDatasetKind.Records, attribution);
            // Capture managed metadata, not JsonElements backed by the disposed manifest.
            _catalogs[id] = new(() => LoadCatalog(prefix + id + ".json", info));
        }
        _widths = new(LoadWidths);
    }

    private Dictionary<string, ReferenceRecord> LoadCatalog(string resource, ReferenceDatasetInfo info)
    {
        using var json = Read(resource);
        return json.RootElement.GetProperty("records").EnumerateArray().Select(r =>
            new ReferenceRecord(r.GetProperty("id").GetString()!, r.GetProperty("name").GetString()!)
            {
                Category = r.TryGetProperty("category", out var category) ? category.GetString() : null,
                Description = r.TryGetProperty("description", out var description) ? description.GetString() : null,
                Source = r.TryGetProperty("source", out var source) ? source.GetString() : null,
                AssetId = r.TryGetProperty("assetId", out var asset) ? Pack.Id + ":" + asset.GetString() : null,
                Attributes = r.GetProperty("attributes").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal),
                Dataset = info,
            }).ToDictionary(r => r.Key, StringComparer.Ordinal);
    }

    private Dictionary<string, (int Version, int Bits)[]> LoadWidths()
    {
        var widths = new Dictionary<string, (int Version, int Bits)[]>(StringComparer.Ordinal);
        foreach (var record in Records(ItemCatalogs.Widths))
        {
            using var versions = JsonDocument.Parse(record.Attributes["versions"]);
            widths[record.Key] = versions.RootElement.EnumerateArray()
                .Select(v => (v.GetProperty("version").GetInt32(), v.GetProperty("bits").GetInt32())).ToArray();
        }
        return widths;
    }

    private static JsonDocument Read(string name)
    {
        var assembly = typeof(SerialDatabase).Assembly;
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(n => n.Replace('\\', '/') == name);
        using var stream = (resource is null ? null : assembly.GetManifestResourceStream(resource))
            ?? throw new InvalidOperationException($"Missing Gearbox reference resource {name}.");
        return JsonDocument.Parse(stream);
    }

    public ReferenceRecord? Find(string catalog, string id) => _catalogs[catalog].Value.GetValueOrDefault(id);
    public ReferenceRecord? TryFind(string catalog, string id) => _catalogs.TryGetValue(catalog, out var records) ? records.Value.GetValueOrDefault(id) : null;
    public IEnumerable<ReferenceRecord> Records(string catalog) => _catalogs[catalog].Value.Values;

    public int Width(string category, int version)
    {
        if (version < 0 || version > MaximumVersion || !_widths.Value.TryGetValue(category, out var versions))
            throw new SaveFormatException("Unsupported item database version or part category.");
        var width = versions[0].Bits;
        foreach (var entry in versions)
            if (entry.Version <= version) width = entry.Bits;
        return width;
    }

    public SerialAsset Asset(string category, int index)
    {
        var record = Find(ItemCatalogs.Indices, $"{category}:{index.ToString(CultureInfo.InvariantCulture)}");
        return new(category, index, record?.Attributes.GetValueOrDefault("asset"));
    }
}

internal sealed record SerialAsset(string Category, int Index, string? Path)
{
    public int Offset { get; init; }
    public int Width { get; init; }
    public string Key => $"{Category}:{Index.ToString(CultureInfo.InvariantCulture)}";
}
