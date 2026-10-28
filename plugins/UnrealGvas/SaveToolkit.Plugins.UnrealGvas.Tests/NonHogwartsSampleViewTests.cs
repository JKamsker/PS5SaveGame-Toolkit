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
            var first = session.QueryCollection(collection.Id, new() { Limit = 200 });
            Assert.Equal(collection.TotalCount, first.TotalCount);
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(first).Length < 2 * 1024 * 1024);
            Assert.Equal(first.Rows.Count, first.Rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.All(first.Rows.SelectMany(row => row.Cells.Values), cell => Assert.IsType<TextNode>(cell));
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
