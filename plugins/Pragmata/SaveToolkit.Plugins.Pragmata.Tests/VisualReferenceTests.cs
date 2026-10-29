using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class VisualReferenceTests
{
    private const string ResourceRoot = "refpacks/pragmata.evidence.en/";
    private const int ExpectedBinaryBytes = 99_634;

    [Fact]
    public void EveryEmbeddedAssetMatchesItsHashTypeDimensionsAndBudget()
    {
        var plugin = new PragmataPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, "PPSA02530");
        var assembly = typeof(PragmataPlugin).Assembly;
        using var manifestStream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.Replace('\\', '/') == ResourceRoot + "pack.json"))!;
        using var manifest = JsonDocument.Parse(manifestStream);
        var assetPaths = new HashSet<string>(StringComparer.Ordinal);
        var totalBytes = 0;
        foreach (var dataset in manifest.RootElement.GetProperty("datasets").EnumerateArray())
        {
            if (dataset.GetProperty("kind").GetString() != "assets") continue;
            foreach (var entry in dataset.GetProperty("files").EnumerateArray())
            {
                var path = entry.GetProperty("path").GetString()!;
                Assert.True(assetPaths.Add(path));
                using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
                    .Single(name => name.Replace('\\', '/') == ResourceRoot + path))!;
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
                var id = $"pragmata.evidence.en:{path}";
                var asset = Assert.IsType<AssetContent>(context.GetAsset(id));
                Assert.Equal("image/webp", asset.ContentType);
                Assert.Equal(bytes, asset.Content.ToArray());
                Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
                var extended = bytes.AsSpan(12, 4).SequenceEqual("VP8X"u8);
                var width = extended ? ReadUInt24(bytes.AsSpan(24)) + 1 : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26)) & 0x3fff;
                var height = extended ? ReadUInt24(bytes.AsSpan(27)) + 1 : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28)) & 0x3fff;
                if (path.StartsWith("icons/", StringComparison.Ordinal))
                    Assert.Equal((64, 64), (width, height));
                else
                {
                    Assert.Equal((2048, 585), (width, height));
                    Assert.InRange(bytes.Length, 1, 600_000);
                }
                var provenance = context.AssetProvenance(id)!;
                Assert.Contains("Capcom", provenance.Attribution.Author, StringComparison.Ordinal);
                Assert.Contains("owner decision 2026-10-29", provenance.Attribution.License, StringComparison.Ordinal);
                totalBytes += bytes.Length;
            }
        }
        Assert.Equal(30, assetPaths.Count);
        Assert.Equal(ExpectedBinaryBytes, totalBytes);
        Assert.InRange(totalBytes, 1, 4_000_000);
    }

    [Fact]
    public void IllustratedRecordsResolveIconsAndKeepFileKeysOutOfSaveItemCatalog()
    {
        var plugin = new PragmataPlugin();
        var store = new ReferencePackStore([plugin]);
        var context = store.Pin(plugin, "PPSA02530");
        var catalog = context.Catalog(IllustratedReferences.CatalogId)!;
        var records = catalog.Search(new ReferenceQuery { Limit = 200 }).Items;
        Assert.Equal(29, records.Count);
        Assert.Equal(15, records.Count(record => record.Category != "Item"));
        Assert.Equal(9, records.Count(record => record.Description!.StartsWith("Official description excerpt", StringComparison.Ordinal)));
        Assert.Equal(6, records.Count(record => record.Description!.StartsWith("Wiki summary", StringComparison.Ordinal)));
        Assert.All(records, record =>
        {
            Assert.StartsWith("wiki-file:", record.Key, StringComparison.Ordinal);
            Assert.Null(context.Catalog(ReferenceReadouts.ItemsCatalog)!.Find(record.Key));
            Assert.False(uint.TryParse(record.Key, out _));
            Assert.NotNull(context.GetAsset(record.AssetId!));
            Assert.NotNull(context.AssetProvenance(record.AssetId!));
            Assert.Contains("CC-BY-SA-3.0", record.AttributionText, StringComparison.Ordinal);
            Assert.Null(record.Limit);
        });
        var secondContext = store.Pin(plugin, "PPSA02530");
        Assert.Same(catalog, secondContext.Catalog(IllustratedReferences.CatalogId));
        Assert.Same(context.GetAsset(records[0].AssetId!), secondContext.GetAsset(records[0].AssetId!));
        Assert.Equal(43, context.Catalog(ReferenceReadouts.MissionsCatalog)!.Count);
        // Resource names now have separate ID evidence; artwork still has no save-ID crosswalk.
        Assert.All(context.Catalog(ReferenceReadouts.ItemsCatalog)!.Search(new ReferenceQuery { Limit = 200 }).Items,
            record => Assert.Null(record.AssetId));
    }

    [Fact]
    public void LazyIllustrationsShowAttributedImagesAndBrowseOnlyFieldsWithoutChangingSaveOrHistory()
    {
        var plugin = new PragmataPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02530");
        var file = (DiskSaveFile)SampleEvidenceTests.Files().Single(file => file.FileName == "data010Slot.bin");
        using var document = (PragmataDocument)plugin.Open(file, context);
        using var session = new DocumentSession("illustrations", file, plugin, document, references: context);
        var inventory = ((TabsNode)session.BuildView()).Tabs.Single(tab => tab.Title == "Inventory");
        var lazy = inventory.Children.OfType<SectionNode>().Single(section => section.LazyId!.StartsWith(IllustratedReferences.SectionId + "/", StringComparison.Ordinal));
        Assert.Empty(lazy.Children);
        var expanded = session.Expand(lazy.LazyId!);
        var table = Assert.Single(expanded.OfType<TableNode>());
        Assert.Equal(29, table.Rows.Count);
        foreach (var row in table.Rows)
        {
            var image = Assert.IsType<ImageNode>(row.Cells["icon"]);
            Assert.NotNull(document.GetAsset(image.AssetId));
            Assert.Contains("Capcom", image.Alt, StringComparison.Ordinal);
            var field = Assert.IsType<FieldNode>(row.Cells["name"]);
            Assert.True(Assert.IsType<ReferenceSpec>(field.Spec).ReadOnly);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            Assert.False(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(field.Value.AsString()))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            var detail = Assert.IsType<SectionNode>(row.Cells["details"]);
            Assert.Contains(detail.Children.OfType<TextNode>(), text => text.Text.Contains("CC-BY-SA-3.0", StringComparison.Ordinal));
            Assert.Contains(detail.Children.OfType<TextNode>(), text => text.Text.Contains("Capcom", StringComparison.Ordinal));
        }
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void MissingPacksRetainCheckpointListAndDoNotAdvertiseUnavailableIllustrations()
    {
        using var document = EditingTests.Gameplay();
        var tabs = ((TabsNode)document.BuildView()).Tabs;
        Assert.DoesNotContain(tabs.Single(tab => tab.Title == "Inventory").Children.OfType<SectionNode>(),
            section => section.LazyId!.StartsWith(IllustratedReferences.SectionId + "/", StringComparison.Ordinal));
        var lazy = tabs.Single(tab => tab.Title == "World").Children.OfType<SectionNode>()
            .Single(section => section.LazyId!.StartsWith("spatial/", StringComparison.Ordinal));
        var map = Assert.Single(document.ExpandNode(lazy.LazyId!).OfType<MapNode>());
        SpatialViewValidation.Validate(map);
        Assert.Null(map.Background);
        Assert.False(map.CoordinateEmbeddingPermitted);
        Assert.All(map.Markers, marker => { Assert.Null(marker.X); Assert.Null(marker.Y); });
        Assert.NotEmpty(map.Markers);
    }

    private static int ReadUInt24(ReadOnlySpan<byte> bytes) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes) | bytes[2] << 16;
}
