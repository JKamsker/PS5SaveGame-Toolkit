using System.Security.Cryptography;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class SampleLsmfTests
{
    [Fact]
    public void SuppliedTypedNewAgeMatchesIndependentPythonCensus()
    {
        var package = LspkPackage.Read(Save().ReadAllBytes());
        var globals = LsfReader.Read(package.Extract(Assert.Single(package.Entries, entry => entry.Name == "Globals.lsf")));
        var root = Assert.Single(globals.Nodes, node => node.Name == "NewAge");
        var attribute = globals.Attributes[Assert.Single(root.Attributes)];
        Assert.Equal(LarianAttributeType.ScratchBuffer, attribute.Type);
        var model = LsmfReader.Read(attribute.Data);
        Assert.Equal(2_618_936, model.Bytes.Length);
        Assert.Equal(2_592_720, model.DirectoryOffset);
        Assert.Equal("fb308dc5c5af0e97e11c41e8212677e43a33c4350627b889ebf27df2c71708cc",
            Convert.ToHexStringLower(SHA256.HashData(model.Bytes.Span)));
        Assert.Equal(315, model.Components.Count);
        foreach (var (name, width, rows) in new (string, int, int)[]
        {
            ("core.v0.EntityId", 16, 9453),
            ("game.stats.v3.StatsComponent", 36, 256),
            ("game.stats.v0.HealthComponent", 32, 380),
            ("game.inventory.v0.ContainerSlotData", 16, 638),
        })
        {
            var component = Assert.Single(model.Components, component => component.Name == name);
            Assert.Equal((width, rows), (component.ElementBytes, component.Rows));
        }

        var entities = model.Entities();
        Assert.Equal(9453, entities.Count);
        Assert.Equal(1984, entities.Select(entity => entity.Guid).Distinct().Count());
        Assert.Equal(1460, entities.GroupBy(entity => entity.Guid).Count(group => group.Count() > 1));
        Assert.Equal(99, entities.Max(entity => entity.Copies));
        Assert.Equal(9453, entities.Select(entity => entity.Key).Distinct().Count());
    }

    [Fact]
    public void SuppliedSavedItemsResolveOnlyExactKnownTemplatesAndNeverProduceAGoldTotal()
    {
        var (plugin, _, references) = ReferenceViewTests.References();
        var save = Save();
        using var document = plugin.Open(save, references);
        var inventory = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Inventory");
        Assert.Empty(inventory.Children.OfType<SectionNode>());
        var table = Assert.Single(inventory.Children.OfType<QueryableTableNode>());
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var pageCount = (int)((table.TotalCount + QueryableCollectionLimits.MaxPageSize - 1) / QueryableCollectionLimits.MaxPageSize);
        var rows = Enumerable.Range(0, pageCount).SelectMany(page => queryable.Query(table.CollectionId,
            new QueryableCollectionQuery { Offset = page * QueryableCollectionLimits.MaxPageSize,
                Limit = QueryableCollectionLimits.MaxPageSize }).Rows).ToArray();
        Assert.Equal(761, rows.Length);
        Assert.Equal(761, rows.Select(row => row.Key).Distinct().Count());
        Assert.Equal(40, rows.Count(row => Assert.IsType<FieldNode>(row.Cells["name"]).Confidence == Confidence.Inferred));
        Assert.Equal(20, rows.Count(row => Assert.IsType<FieldNode>(row.Cells["name"]).Value.AsString() == "Potion of Healing"));
        Assert.Equal(4, rows.Count(row => Assert.IsType<FieldNode>(row.Cells["name"]).Value.AsString() == "Gold"));
        Assert.All(rows, row => Assert.Equal("Unknown / unknown", Assert.IsType<TextNode>(row.Cells["ownership"]).Text));
        Assert.Equal(save.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void F1CorpusPinsOneGenerationAndIdenticalBackupTwinsWithoutPretendingThereIsAnEventPair()
    {
        var set = SampleSaves.Load("PPSA18463");
        Assert.All(set.AllDiskFiles, file => Assert.Equal(0, file.Directory.Generation));
        var manifest = FixtureManifest.Load(Path.Combine(AppContext.BaseDirectory, "fixtures.json"));
        Assert.Empty(manifest.Ignore);
        var fixture = Assert.Single(manifest.Fixtures);
        Assert.False(fixture.Control);
        var result = FixtureVerifier.Verify(fixture, set, set, FixtureVerifier.OptionsFor(fixture, manifest, decoder: null));
        Assert.True(result.Passed, string.Join('\n', result.Errors.Concat(result.Unexpected)));
        Assert.False(result.Report.HasChanges);
        Assert.Equal(1, result.Report.Before.Generations);
        Assert.Equal(1, result.Report.Before.Twins.Identical);
        Assert.Empty(result.Report.Before.Twins.Mismatches);
        // F1 short-circuits identical before/after fingerprints before backup collapsing.
        Assert.Equal(2, result.Report.Totals.Unchanged);
        Assert.Equal(0, result.Report.Totals.MirrorsMain);
    }

    private static ISaveFile Save() => SampleSaves.Load("PPSA18463").AllDiskFiles.First(file =>
        !file.Directory.IsBackup && file.FileName == "AutoSave_0.lsv");
}
