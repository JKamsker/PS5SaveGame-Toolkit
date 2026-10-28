using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Factual runtime identities and explicitly cross-game reference candidates.</summary>
internal sealed class RagnarokCommunityTables
{
    public const string RuntimeCatalog = "gow-ragnarok-runtime";
    public const string TreeCatalog = "gow-ragnarok-trees";
    public const string OlderCatalog = "gow-ragnarok-2018";
    private const string Build = "PS5 revision 0x00513040";
    private static readonly IReadOnlyDictionary<string, ReferenceRecord> RuntimeFallback =
        RagnarokReferenceLoader.Load("gow-ragnarok.runtime.en");
    private static readonly IReadOnlyDictionary<string, ReferenceRecord> OlderFallback =
        RagnarokReferenceLoader.Load("gow-ragnarok.2018.en");
    private readonly IReferenceCatalog? _runtime;
    private readonly IReferenceCatalog? _trees;
    private readonly IReferenceCatalog? _older;

    public RagnarokCommunityTables(IReferenceContext context)
    {
        _runtime = context.Catalog(RuntimeCatalog, Build);
        _trees = context.Catalog(TreeCatalog, Build);
        _older = context.Catalog(OlderCatalog, Build);
    }

    public ReferenceRecord? Runtime(string key)
    {
        var record = _runtime?.Find(key) ?? RuntimeFallback.GetValueOrDefault(key);
        return record?.Dataset.Id == RuntimeCatalog ? record : null;
    }
    public ReferenceRecord? Older(string key) => _older?.Find(key) ?? OlderFallback.GetValueOrDefault(key);
    public ReferenceRecord? Tree(ulong id)
    {
        var key = RagnarokReferences.Key(id);
        var record = _trees?.Find(key) ?? RuntimeFallback.GetValueOrDefault(key);
        return record?.Dataset.Id == TreeCatalog ? record : null;
    }
}
