using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal sealed class AstroReferences(IReferenceContext context, string fingerprint)
{
    public const string Collectibles = "astro_collectibles";
    public const string Worlds = "astro_worlds";
    public const string Captions = "astro_captions";
    public const string InspectActionId = "collectibles/inspect";
    private readonly IReferenceCatalog? _collectibles = BuildAgnostic(context.Catalog(Collectibles));
    private readonly IReferenceCatalog? _worlds = BuildAgnostic(context.Catalog(Worlds));
    private readonly IReferenceCatalog? _captions = BuildAgnostic(context.Catalog(Captions));
    private readonly Dictionary<string, ReferenceRecord?> _worldCache = new(StringComparer.Ordinal);

    public ReferenceRecord? Collectible(string group, string key) => Describe(Compatible(_collectibles?.Find($"{group}/{key}")));
    public ReferenceRecord? World(string path)
    {
        if (_worldCache.TryGetValue(path, out var cached)) return cached;
        var record = Compatible(_worlds?.Find(path));
        _worldCache.Add(path, record);
        return record;
    }
    public AssetContent? Asset(string assetId) => context.GetAsset(assetId);
    public ReferenceDatasetInfo? AssetProvenance(string assetId) => context.AssetProvenance(assetId);

    public bool Handles(string actionId) => actionId == InspectActionId;

    public ActionOutcome Inspect(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var catalog = _collectibles ?? throw new InvalidOperationException("The ASTRO catalogue pack is unavailable.");
        if (!parameters.TryGetValue("record", out var value) || value.Kind != FieldValueKind.Text ||
            Compatible(catalog.Find(value.AsString())) is not { } record)
        {
            throw new ArgumentException("Choose the existing ASTRO catalogue record.", nameof(parameters));
        }

        return new ActionOutcome
        {
            Message = $"{record.Name} · {record.Key} · {record.Category ?? "Uncategorised"}\n" +
                $"{Describe(record)?.Description}\n{record.AttributionText}\n" +
                "Inspection only; saved ownership and gameplay effects remain unknown. No bytes changed."
        };
    }

    private ReferenceRecord? Describe(ReferenceRecord? record)
    {
        if (record is null) return null;
        var description = record.Description;
        if (_captions?.Find(record.Key) is { } caption)
            description += $" Official in-game caption: {caption.Description} {caption.AttributionText}";
        if (record.AssetId is { } asset && context.AssetProvenance(asset) is { } source)
            description += $" Thumbnail: {source.Attribution.ToShortText()}";
        return record with { Description = description };
    }

    private ReferenceRecord? Compatible(ReferenceRecord? record) =>
        record?.Attributes.TryGetValue("schemaFingerprint", out var schema) == true && schema == fingerprint ? record : null;

    // The samples carry no trusted game-build label. Do not pick build-specific data by guessing.
    private static IReferenceCatalog? BuildAgnostic(IReferenceCatalog? catalog) =>
        catalog?.Pack.GameBuilds.Count == 0 ? catalog : null;
}
