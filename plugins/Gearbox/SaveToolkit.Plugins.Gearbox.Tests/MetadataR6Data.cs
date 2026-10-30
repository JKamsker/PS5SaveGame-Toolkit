using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

internal static class MetadataR6Data
{
    internal static MemorySaveSet Set(GearboxDocument character, GearboxDocument profile) =>
        new MemorySaveSet().With(character.Root.Schema.Wonderlands ? "PPSA01493" : "PPSA01463", "R6",
            new Dictionary<string, byte[]>
            {
                ["1"] = character.Serialize().Single().Content.ToArray(),
                ["profile"] = profile.Serialize().Single().Content.ToArray(),
            });

    internal static GearboxGroupDocument Group(MemorySaveSet set, IReferenceContext? references = null) =>
        (GearboxGroupDocument)new GearboxPlugin().OpenGroup(new(((ISaveSet)set).AllFiles.Select(file =>
            new DocumentGroupFile(file.FileName == "profile" ? "profile" : "character", file)).ToArray()),
            references ?? IReferenceContext.Empty);

    internal static Workspace Workspace(MemorySaveSet source)
    {
        var sources = ((ISaveSet)source).AllFiles.Select(file => new ImportSource(file.RelativePath, file.OpenRead));
        var set = SaveSetImporter.Import(sources, Path.Combine(TestScratch.ProcessFolder, Guid.NewGuid().ToString("N")));
        var plugin = new GearboxPlugin();
        return new Workspace("metadata-r6", "Metadata R6", set, new PluginCatalog([plugin]), new ReferencePackStore([plugin]));
    }

    internal sealed class Context(ReferenceRecord record) : IReferenceContext
    {
        private readonly IReferenceCatalog _catalog = new CatalogRecord(record);
        public IReadOnlyList<ReferencePackInfo> Packs => [_catalog.Pack];
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) =>
            catalogId == GearboxWorldCatalog.CatalogId ? _catalog : null;
        public AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }

    private sealed class CatalogRecord(ReferenceRecord record) : IReferenceCatalog
    {
        public string Id => GearboxWorldCatalog.CatalogId;
        public ReferencePackInfo Pack => SerialDatabase.For(true).Pack;
        public ReferenceDatasetInfo Dataset => record.Dataset;
        public int Count => 1;
        public IReadOnlyList<string> Categories => [];
        public ReferenceRecord? Find(string key) => key == record.Key ? record : null;
        public ReferencePage Search(ReferenceQuery query) => new([record], 1, 0);
    }
}
