using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed class RagnarokSkillReferences(IReferenceContext context)
{
    internal const string CatalogId = "gow-ragnarok-skills";
    private static readonly IReadOnlyDictionary<string, ReferenceRecord> Fallback =
        RagnarokReferenceLoader.Load("gow-ragnarok.skills.en");
    private readonly IReferenceCatalog? _catalog = context.Catalog(CatalogId, "PS5 revision 0x00513040");

    // Presentation keys are deliberately separate from unproven saved u64 hashes.
    // A user pack may enrich these references; new keys cannot invent saved-state mappings.
    public IReadOnlyList<ReferenceRecord> Weapon(string weapon) => Fallback.Values
        .Where(record => record.Attributes["weapon"] == weapon)
        .Select(record => _catalog?.Find(record.Key) ?? record).ToArray();
}
