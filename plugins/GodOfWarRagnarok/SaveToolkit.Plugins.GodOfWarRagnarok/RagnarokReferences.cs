using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Resolves the optional F2 catalog while retaining the small embedded fallback used by direct plugin callers.</summary>
internal sealed class RagnarokReferences
{
    public const string CatalogId = "gow-ragnarok-resources";
    public const string AssetCatalogId = "gow-ragnarok-assets";
    public const string ContainerSources =
        "Hero: Continue? contributors, GPL-3.0, https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py ; " +
        "Blacksmith chest: SaveWizard-Resources contributors, GPL-3.0, https://github.com/sterben-Dev10/SaveWizard-Resources/blob/4c32596efde54af7bec390b298265f09c929d94d/SaveWizard-Codes/GOW%20Ragnarok%20%5BSaveWizard%5D.txt .";
    private const ulong XpCandidateId = 0x9C7CF61C839B9A8A;
    private const string Build = "PS5 revision 0x00513040";
    private static readonly IReadOnlyDictionary<string, ReferenceRecord> OriginalFallback =
        RagnarokReferenceLoader.Load("gow-ragnarok.ps5.en");
    private readonly IReferenceCatalog? _catalog;
    private readonly IReferenceCatalog? _collectibles;
    private readonly IReferenceCatalog? _evidence;
    private readonly IReferenceCatalog? _assets;
    private readonly IReferenceContext _context;
    private readonly Dictionary<ulong, ResolvedReference> _resolved = [];
    public RagnarokSkillReferences Skills { get; }
    public RagnarokCommunityTables Community { get; }
    public RagnarokGearReferences Gear { get; }

    public RagnarokReferences(IReferenceContext context)
    {
        _context = context;
        Skills = new(context);
        Community = new(context);
        Gear = new(context);
        _catalog = context.Catalog(CatalogId, Build);
        _collectibles = context.Catalog("gow-ragnarok-collectibles", Build);
        _evidence = context.Catalog("gow-ragnarok-community-ids", Build);
        _assets = context.Catalog(AssetCatalogId, Build);
    }

    public ReferenceRecord? Find(ulong id) => Resolve(id).Record;

    private ResolvedReference Resolve(ulong id)
    {
        if (_resolved.TryGetValue(id, out var resolved)) return resolved;
        var record = FindUncached(id);
        resolved = new(record, record?.Name ?? $"Unknown ID 0x{id:X16}", record?.AttributionText ??
            "No sourced name mapping is available; the raw ID is preserved.");
        _resolved.Add(id, resolved);
        return resolved;
    }

    private ReferenceRecord? FindUncached(ulong id)
    {
        var key = Key(id);
        var runtime = Community.Runtime(key);
        var community = RagnarokCommunityNames.Find(key);
        var original = OriginalFallback.GetValueOrDefault(key);
        return _catalog?.Find(key) ?? original ?? _collectibles?.Find(key) ??
            (community?.Category == "collectible" ? community : null) ??
            (runtime?.Category != "Unknown" ? runtime : null) ??
            _evidence?.Find(key) ?? community ?? runtime ?? Community.Older(key);
    }

    public string Name(ulong id) => Resolve(id).Name;

    public string Category(ulong id) => id == XpCandidateId ? "XP" : Find(id)?.Category ?? "Unknown";

    public string Container(ulong id) => id switch
    {
        RagnarokFormat.HeroWalletId => "Hero wallet (inferred)",
        0x72B62D37E2D5BDB0 => "Blacksmith chest (inferred)",
        _ => $"Unknown container 0x{id:X16}",
    };

    public string Attribution(ulong id) => Resolve(id).Attribution;

    public ReferenceRecord? AssetRecord(string id) => _assets?.Find(id);

    public AssetContent? GetAsset(string assetId) => _context.GetAsset(assetId);

    public ReferenceDatasetInfo? AssetProvenance(string assetId) => _context.AssetProvenance(assetId);

    public string? Catalog(ulong id) => Find(id)?.Dataset.Id is { } catalog &&
        catalog is CatalogId or "gow-ragnarok-collectibles" or "gow-ragnarok-community-ids" or
            RagnarokCommunityTables.RuntimeCatalog or RagnarokCommunityTables.OlderCatalog ? catalog : null;

    public static string Key(ulong id)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, id);
        return Convert.ToHexString(bytes);
    }

    private sealed record ResolvedReference(ReferenceRecord? Record, string Name, string Attribution);
}
