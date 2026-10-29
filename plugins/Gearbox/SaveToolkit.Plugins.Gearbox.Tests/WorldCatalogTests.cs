using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class WorldCatalogTests
{
    [Theory]
    [InlineData("PPSA01463", 1387, 208, 100)]
    [InlineData("PPSA01493", 459, 90, 0)]
    public void AddedDatasets_ArePinnedAttributedQueryableAndDoNotChangeSamples(string title, int total, int missions, int decorations)
    {
        var plugin = new GearboxPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var references = store.Pin(plugin, title);
        var catalog = references.Catalog(GearboxWorldCatalog.CatalogId)!;
        Assert.Equal(total, catalog.Count);
        Assert.Equal("Zlib", catalog.Dataset.Attribution.License);
        Assert.Equal("1.3.0", catalog.Pack.Version);
        Assert.Equal(missions, catalog.Search(new() { Category = "mission" }).Total);
        Assert.Equal(decorations, catalog.Search(new() { Category = "room-decoration" }).Total);
        var file = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == "1");
        foreach (var context in new[] { IReferenceContext.Empty, references })
        {
            using var document = plugin.Open(file, context);
            var queryable = (IQueryableDocument)document;
            var collection = queryable.Collections.Single(c => c.Id == GearboxWorldCatalog.CatalogId);
            Assert.Equal(total, collection.TotalCount);
            var page = queryable.Query(collection.Id, new() { Offset = 200, Limit = 7, SortColumn = "name" });
            Assert.Equal(7, page.Rows.Count);
            foreach (var row in page.Rows)
            {
                var name = Assert.IsType<FieldNode>(row.Cells["name"]);
                Assert.True(name.Spec.ReadOnly);
                Assert.Equal(Confidence.Inferred, name.Confidence);
                Assert.Contains("Zlib", name.Description);
                Assert.Equal(name.Value, document.GetValue(name.ValueId));
            }
            var filtered = queryable.Query(collection.Id, new() { Filters = new Dictionary<string, string> { ["category"] = "mission" }, Limit = 200 });
            Assert.Equal(missions, filtered.FilteredCount);
            Assert.All(filtered.Rows, row => Assert.Equal("mission", Assert.IsType<TextNode>(row.Cells["category"]).Text));
            var knownId = Assert.IsType<FieldNode>(page.Rows[0].Cells["id"]).Value.AsString();
            Assert.Contains(queryable.Query(collection.Id, new() { Text = knownId }).Rows, row => Assert.IsType<FieldNode>(row.Cells["id"]).Value.AsString() == knownId);
            Assert.Throws<ArgumentException>(() => queryable.Query(collection.Id, new() { SortColumn = "not-a-column" }));
            Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        }
    }

    [Theory]
    [InlineData(false, "/Game/Missions/Plot/Mission_Ep01_ChildrenOfTheVault.Mission_Ep01_ChildrenOfTheVault_C", "Children of the Vault")]
    [InlineData(true, "/Game/Missions/Plot/Mission_Plot00.Mission_Plot00_C", "Bunkers & Badasses")]
    public void MissionAndClassLabels_NormalizeDocumentedIdsAndPreserveUnknowns(bool wonderlands, string path, string name)
    {
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        var container = GearboxContainer.Parse(ContainerTests.Create(wonderlands, false), title, false);
        using var document = new GearboxDocument("1", title, container, container.Decode(), IReferenceContext.Empty);
        var field = document.World.Label("test/mission", "Mission", path, mission: true);
        Assert.Equal(path.Split('.')[0].ToLowerInvariant(), field.Value.AsString());
        Assert.Contains(name + " — " + path, field.Description);
        Assert.Contains("Zlib", field.Description);
        Assert.True(field.Spec.ReadOnly);
        const string unknown = "/Game/Unknown.Asset_C";
        var fallback = document.World.Label("test/unknown", "Unknown", unknown, mission: true);
        Assert.Equal(unknown, fallback.Value.AsString());
        Assert.Equal(Confidence.Unknown, fallback.Confidence);
        Assert.Null(fallback.Description);
    }
}
