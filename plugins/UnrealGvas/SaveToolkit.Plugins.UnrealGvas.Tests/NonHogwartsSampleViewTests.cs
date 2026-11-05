using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class NonHogwartsSampleViewTests
{
    private const int MaxPageBytes = 2 * 1024 * 1024;

    [Theory]
    [InlineData("PPSA01288", "General", "SaveGame", "Costumes and emotes")]
    [InlineData("PPSA02199", "ue4savegame.dpx.sav", "SaveGame00", "Actors")]
    [InlineData("PPSA02199", "ue4savegame.dpx.sav", "GameUserSettings", "Settings")]
    public void HostRegistersNamedCollectionsAndSourcesWithoutChangingBytes(string title, string name, string directory, string tab)
    {
        var sample = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == name && file.Directory.Name == directory);
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, title);
        using var document = plugin.Open(sample, references);
        using var session = new DocumentSession("non-hogwarts", sample, plugin, plugin.Open(sample, references), references: references);
        Assert.Contains(Assert.IsType<TabsNode>(session.BuildView()).Tabs, item => item.Title == tab);
        Assert.True(session.QueryableCollections.Count > 1);
        foreach (var collection in session.QueryableCollections)
        {
            var first = session.QueryCollection(collection.Id, new() { Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(collection.TotalCount, first.TotalCount);
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(first).Length < MaxPageBytes);
            Assert.Equal(first.Rows.Count, first.Rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
            AssertHostCells(first);
        }
        Assert.True(session.CanEdit);
        if (tab == "Actors")
        {
            var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
            var tags = query.Query("jedi/actor-tags", new() { Filters = new Dictionary<string, string> { ["name"] = "ActorRecords" } });
            Assert.True(tags.TotalCount > 1_000);
            Assert.Contains("Verified storage", ((TextNode)Assert.Single(tags.Rows).Cells["evidence"]).Text);
            var names = query.Query("jedi/actor-names", new() { Text = "ActorRecords" });
            Assert.NotEmpty(names.Rows);
            Assert.All(names.Rows, row => Assert.Contains("Unknown", ((TextNode)row.Cells["evidence"]).Text));
            Assert.True(query.Collections.Single(collection => collection.Id == "jedi/actor-names").TotalCount <= 2_000);
        }
        Assert.Equal(sample.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("settings", "PPSA02199", "Settings")]
    [InlineData("jedi", "PPSA02199", "Actors")]
    [InlineData("sackboy", "PPSA01288", "Costumes and emotes")]
    public void SyntheticTitleCollectionsResolveCanonicalCellsWithoutChangingBytes(string kind, string title, string tab)
    {
        var bytes = kind switch
        {
            "settings" => TitleFixture.Settings(),
            "jedi" => TitleFixture.Scalars("jedi"),
            _ => SackboyCostumes(),
        };
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, title);
        using var import = new TestSaveImport(bytes, $"{title}/sdimg_Test/save.sav");
        using var document = plugin.Open(import.File, references);
        using var session = new DocumentSession("synthetic-projections", import.File, plugin,
            plugin.Open(import.File, references), references: references);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        var resolvedFields = new List<FieldNode>();
        Assert.Equal(PluginMaturity.Experimental, plugin.Info.Maturity);
        Assert.True(session.CanEdit);
        Assert.Contains(Assert.IsType<TabsNode>(session.BuildView()).Tabs, item => item.Title == tab);
        Assert.True(session.QueryableCollections.Count > 1);

        foreach (var collection in session.QueryableCollections)
        {
            var query = new QueryableCollectionQuery { Limit = QueryableCollectionLimits.MaxPageSize };
            var raw = queryable.Query(collection.Id, query);
            var hosted = session.QueryCollection(collection.Id, query, null, TestContext.Current.CancellationToken);
            AssertHostCells(hosted);
            Assert.Equal(raw.Rows.Select(row => row.Key), hosted.Rows.Select(row => row.Key));
            Assert.Equal(raw.TotalCount, hosted.TotalCount);
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(hosted).Length < MaxPageBytes);
            foreach (var (source, target) in raw.Rows.Zip(hosted.Rows))
            {
                if (source.Cells["value"] is FieldReferenceNode reference)
                {
                    var field = Assert.IsType<FieldNode>(target.Cells["value"]);
                    var canonical = Assert.Single(metadata.ResolveFieldMetadata([reference.TargetValueId]));
                    Assert.Equal(reference.TargetValueId, field.ValueId);
                    Assert.Equal(document.GetValue(field.ValueId), field.Value);
                    Assert.Equal(canonical.Label, field.Label);
                    Assert.Equal(canonical.Spec, field.Spec);
                    Assert.Equal(canonical.Presentation?.Confidence, field.Confidence);
                    Assert.Equal(canonical.Presentation?.Description, field.Description);
                    Assert.Equal(canonical.Presentation?.Warning, field.Warning);
                    resolvedFields.Add(field);
                }
                else
                    Assert.Equal(Assert.IsType<TextNode>(source.Cells["value"]), target.Cells["value"]);
            }
        }
        Assert.Contains(resolvedFields, field => !field.Spec.ReadOnly);
        if (kind != "settings") Assert.Contains(resolvedFields, field => field.Spec.ReadOnly);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Empty(session.Serialize(TestContext.Current.CancellationToken));
    }

    private static void AssertHostCells(QueryableCollectionPage page)
    {
        Assert.All(page.Rows.SelectMany(row => row.Cells.Where(pair => pair.Key != "value").Select(pair => pair.Value)),
            cell => Assert.IsType<TextNode>(cell));
        // DocumentSession resolves available canonical references before returning a page to the UI.
        Assert.All(page.Rows, row =>
            Assert.True(row.Cells["value"] is TextNode or FieldNode,
                $"Unexpected value cell type in {page.CollectionId}: {row.Cells["value"].GetType().Name}."));
    }

    private static byte[] SackboyCostumes() => GvasFixture.Build(writer =>
    {
        const int wideTextLength = 256; // Exceeds the 192-character collection-cell fallback threshold.
        GvasFixture.Property(writer, "CollectabellsStored", "IntProperty", GvasFixture.Body(value => value.Write(TitleFixture.InitialCount)));
        GvasFixture.Property(writer, "CostumeSaveData", "StructProperty", GvasFixture.Body(body =>
        {
            GvasFixture.Property(body, "AuthoredCount", "IntProperty", GvasFixture.Body(value => value.Write(TitleFixture.InitialCount)));
            GvasFixture.Property(body, "Opaque", "FutureProperty", [0xFE, 0xED]);
            GvasFixture.Property(body, "Wide", "StrProperty", GvasFixture.Body(value =>
                GvasFixture.String(value, new string('雪', wideTextLength))));
            GvasFixture.String(body, "None");
        }), "AuthoredCostumes");
        GvasFixture.Property(writer, "Checksum", "UInt32Property", [0x78, 0x56, 0x34, 0x12]);
    }, minor: 25, patch: 1, packageVersion: 518, saveClass: "/Script/GingerBread.GingerbreadSaveUser");

    [Fact]
    public void NamedViewsHaveAttributedStorageCaptionsAndUnknownIdsStayRaw()
    {
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01288");
        var file = SampleSaves.Load("PPSA01288").AllDiskFiles.First(file => file.FileName == "General");
        using var document = plugin.Open(file, references);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var currency = queryable.Query("gvas/group/Progression", new() { Filters = new Dictionary<string, string> { ["name"] = "Stored Collectabells" } });
        var row = Assert.Single(currency.Rows);
        Assert.Equal("Stored Collectabells", ((TextNode)row.Cells["name"]).Text);
        Assert.Contains("GPL-3.0-or-later", ((TextNode)row.Cells["source"]).Text);
        Assert.Contains("https://wiki.imagisphere.me/", ((TextNode)row.Cells["source"]).Text);
        Assert.Contains("Unknown", ((TextNode)row.Cells["evidence"]).Text);
        Assert.Null(references.Catalog("unrealgvas_sackboy_fields")!.Find("invented-id"));
        var fields = GvasFormatTests.Expand(document);
        Assert.Contains(fields, field => field.Label.Contains("Checksum", StringComparison.Ordinal) && field.Spec.ReadOnly);
        Assert.Contains(fields, field => !field.Spec.ReadOnly);
    }

    [Theory]
    [InlineData("trailer")]
    [InlineData("declared-small")]
    [InlineData("declared-large")]
    [InlineData("count")]
    public void CorruptActorStreamsDisableTheirOverlayAndKeepOriginalBytes(string corruption)
    {
        var sample = SampleSaves.Load("PPSA02199").AllDiskFiles.First(file => file.Directory.Name == "SaveGame00" && file.FileName == "ue4savegame.dpx.sav");
        var bytes = sample.ReadAllBytes();
        var parsed = GvasFile.Parse(bytes);
        var actor = parsed.Properties.Single(node => node.Name == "CompressedActorRecords");
        var size = parsed.Properties.Single(node => node.Name == "UncompressedActorRecordsSize");
        switch (corruption)
        {
            case "trailer": bytes[actor.Offset + actor.Length - 1] ^= 1; break;
            case "declared-small": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(size.Offset), 1); break;
            case "declared-large": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(size.Offset), GvasFile.MaxFileLength + 1); break;
            case "count": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(actor.Offset), actor.Length - sizeof(int) - 1); break;
        }
        if (corruption == "count")
        {
            var error = Assert.Throws<SaveFormatException>(() => new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save.sav", bytes, "PPSA02199")));
            Assert.Contains("CompressedActorRecords", error.Message);
            Assert.Contains("size", error.Message);
            return;
        }
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save.sav", bytes, "PPSA02199"));
        Assert.False(document.CanEdit);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.DoesNotContain(queryable.Collections, collection => collection.Id == "jedi/actor-tags");
        Assert.Contains(queryable.Collections, collection => collection.Id == "gvas/properties");
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void UnsupportedEngineHeaderFallsBackToGenericWithoutRemovingIntegrityProtection()
    {
        var file = SampleSaves.Load("PPSA02199").AllDiskFiles.First(file => file.Directory.Name == "SaveGame00" && file.FileName == "ue4savegame.dpx.sav");
        var bytes = file.ReadAllBytes();
        const int enginePatchOffset = 16;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(enginePatchOffset), 9);
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save.sav", bytes, "PPSA02199"));
        Assert.False(document.CanEdit);
        Assert.Single(Assert.IsAssignableFrom<IQueryableDocument>(document).Collections);
        Assert.DoesNotContain(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Actors");
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
