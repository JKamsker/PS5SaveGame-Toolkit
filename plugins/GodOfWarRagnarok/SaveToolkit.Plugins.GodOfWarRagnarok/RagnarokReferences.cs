using System.Buffers.Binary;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Resolves the optional F2 catalog while retaining the small embedded fallback used by direct plugin callers.</summary>
internal sealed class RagnarokReferences
{
    public const string CatalogId = "gow-ragnarok-resources";
    public const string ContainerSources =
        "Hero: Continue? contributors, GPL-3.0, https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py ; " +
        "Blacksmith chest: SaveWizard-Resources contributors, GPL-3.0, https://github.com/sterben-Dev10/SaveWizard-Resources/blob/4c32596efde54af7bec390b298265f09c929d94d/SaveWizard-Codes/GOW%20Ragnarok%20%5BSaveWizard%5D.txt .";
    private const ulong XpCandidateId = 0x9C7CF61C839B9A8A;
    private const string Build = "PS5 revision 0x00513040";
    private static readonly ReferenceDatasetInfo FallbackDataset = new(
        "gow-ragnarok.ps5.en", CatalogId, ReferenceDatasetKind.Records,
        new ReferenceAttribution(
            "https://godofwar.fandom.com/wiki/Resources/GOW_%28Ragnar%C3%B6k%29",
            "God of War Wiki contributors", "CC-BY-SA-3.0", "https://www.fandom.com/licensing",
            new DateOnly(2026, 10, 28),
            "Normalized factual names/categories and preserved the save's little-endian hash bytes; legal limits remain unknown."));
    private static readonly ReferenceDatasetInfo ObservationsDataset = new(
        "gow-ragnarok-observations", "xp-candidate", ReferenceDatasetKind.Records,
        new ReferenceAttribution("https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/19",
            "PS5 Save Game Toolkit contributors", "GPL-3.0-or-later", "https://www.gnu.org/licenses/gpl-3.0.html",
            new DateOnly(2026, 10, 28), "Original PS5 byte observations. Generic XP role remains inferred; no third-party code or prose copied."));

    private readonly IReferenceCatalog? _catalog;
    private readonly IReferenceCatalog? _collectibles;
    private readonly IReferenceCatalog? _evidence;

    public RagnarokReferences(IReferenceContext context)
    {
        _catalog = context.Catalog(CatalogId, Build);
        _collectibles = context.Catalog("gow-ragnarok-collectibles", Build);
        _evidence = context.Catalog("gow-ragnarok-community-ids", Build);
    }

    public ReferenceRecord? Find(ulong id)
    {
        var key = Key(id);
        var sourced = _catalog?.Find(key) ?? _collectibles?.Find(key) ?? _evidence?.Find(key) ?? RagnarokCommunityNames.Find(key);
        if (sourced is not null)
        {
            return sourced;
        }

        if (id == XpCandidateId)
        {
            return new(key, ResourceNames.Get(id))
            {
                Category = "XP", Dataset = ObservationsDataset,
                Description = "Raw PS5 wallet counter with an inferred XP role. Character ownership, level effects and legal maximum are unverified.",
            };
        }

        return ResourceNames.TryGet(id, out var name)
            ? new ReferenceRecord(key, name)
            {
                Category = "resource",
                Description = "Embedded fallback identity. The PS5 save stores a 32-bit quantity word; legal game limits are unknown.",
                Source = FallbackDataset.Attribution.SourceUrl,
                Dataset = FallbackDataset,
            }
            : null;
    }

    public string Name(ulong id) => Find(id)?.Name ?? $"Unknown ID 0x{id:X16}";

    public string Category(ulong id) => id == XpCandidateId ? "XP" : Find(id)?.Category ?? "Unknown";

    public string Container(ulong id) => id switch
    {
        RagnarokFormat.HeroWalletId => "Hero wallet (inferred)",
        0x72B62D37E2D5BDB0 => "Blacksmith chest (inferred)",
        _ => $"Unknown container 0x{id:X16}",
    };

    public string Attribution(ulong id) => Find(id)?.AttributionText ??
        "No licensed name mapping is available; the raw ID is preserved.";

    public static string Key(ulong id)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, id);
        return Convert.ToHexString(bytes);
    }
}
