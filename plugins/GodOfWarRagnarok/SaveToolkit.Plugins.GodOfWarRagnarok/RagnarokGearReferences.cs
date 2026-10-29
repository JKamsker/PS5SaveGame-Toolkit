using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed class RagnarokGearReferences(IReferenceContext context)
{
    internal const string CatalogId = "gow-ragnarok-gear";
    private static readonly IReadOnlyDictionary<string, ReferenceRecord> Fallback =
        RagnarokReferenceLoader.Load("gow-ragnarok.gear.en");
    private readonly IReferenceCatalog? _catalog = context.Catalog(CatalogId, "PS5 revision 0x00513040");

    public ReferenceRecord? Find(ulong id)
    {
        var key = RagnarokReferences.Key(id);
        return _catalog?.Find(key) ?? Fallback.GetValueOrDefault(key);
    }
}
