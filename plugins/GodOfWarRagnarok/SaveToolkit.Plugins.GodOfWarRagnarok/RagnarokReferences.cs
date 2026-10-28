using System.Buffers.Binary;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Resolves the optional F2 catalog while retaining the small embedded fallback used by direct plugin callers.</summary>
internal sealed class RagnarokReferences
{
    public const string CatalogId = "gow-ragnarok-resources";
    private const string Build = "PS5 revision 0x00513040";
    private static readonly ReferenceDatasetInfo FallbackDataset = new(
        "gow-ragnarok.ps5.en", CatalogId, ReferenceDatasetKind.Records,
        new ReferenceAttribution(
            "https://godofwar.fandom.com/wiki/Resources/GOW_%28Ragnar%C3%B6k%29",
            "God of War Wiki contributors", "CC-BY-SA-3.0", "https://www.fandom.com/licensing",
            new DateOnly(2026, 10, 28),
            "Normalized factual names/categories and preserved the save's little-endian hash bytes; legal limits remain unknown."));

    private readonly IReferenceCatalog? _catalog;

    public RagnarokReferences(IReferenceContext context) => _catalog = context.Catalog(CatalogId, Build);

    public ReferenceRecord? Find(ulong id)
    {
        var key = Key(id);
        return _catalog?.Find(key) ?? (ResourceNames.TryGet(id, out var name)
            ? new ReferenceRecord(key, name)
            {
                Category = "resource",
                Description = "Embedded fallback identity. The PS5 save stores a 32-bit quantity word; legal game limits are unknown.",
                Source = FallbackDataset.Attribution.SourceUrl,
                Dataset = FallbackDataset,
            }
            : null);
    }

    public string Name(ulong id) => Find(id)?.Name ?? $"Unknown ID 0x{id:X16}";

    public string Attribution(ulong id) => Find(id)?.AttributionText ??
        "No licensed name mapping is available; the raw ID is preserved.";

    public static string Key(ulong id)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, id);
        return Convert.ToHexString(bytes);
    }
}
