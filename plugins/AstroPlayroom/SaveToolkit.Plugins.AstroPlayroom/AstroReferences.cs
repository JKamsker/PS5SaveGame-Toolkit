using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal sealed class AstroReferences(IReferenceContext context, string fingerprint)
{
    public const string Collectibles = "astro_collectibles";
    public const string Worlds = "astro_worlds";
    private readonly IReferenceCatalog? _collectibles = BuildAgnostic(context.Catalog(Collectibles));
    private readonly IReferenceCatalog? _worlds = BuildAgnostic(context.Catalog(Worlds));

    public ReferenceRecord? Collectible(string group, string key) => Compatible(_collectibles?.Find($"{group}/{key}"));
    public ReferenceRecord? World(string path) => Compatible(_worlds?.Find(path));

    private ReferenceRecord? Compatible(ReferenceRecord? record) =>
        record?.Attributes.TryGetValue("schemaFingerprint", out var schema) == true && schema == fingerprint ? record : null;

    // The samples carry no trusted game-build label. Do not pick build-specific data by guessing.
    private static IReferenceCatalog? BuildAgnostic(IReferenceCatalog? catalog) =>
        catalog?.Pack.GameBuilds.Count == 0 ? catalog : null;
}
