using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasFieldReferenceTests
{
    [Fact]
    public void SettingsReferencesUseOneCanonicalMetadataBeforeSearchExpansion()
    {
        var plugin = new UnrealGvasPlugin();
        var file = MemorySaveSet.SingleFile("settings.sav", TitleFixture.Settings(), "PPSA02199");
        using var document = plugin.Open(file);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        Assert.True(document.CanEdit);

        var raw = queryable.Query("gvas/properties", new() { Text = "HDRBrightness", Limit = 1 });
        var settings = queryable.Query("jedi/settings", new() { Text = "HDRBrightness", Limit = 1 });
        Assert.Equal("property/0", Assert.IsType<FieldReferenceNode>(Assert.Single(raw.Rows).Cells["value"]).TargetValueId);
        Assert.Equal("property/0", Assert.IsType<FieldReferenceNode>(Assert.Single(settings.Rows).Cells["value"]).TargetValueId);
        document.BuildView();
        var searchTable = Assert.Single(document.ExpandNode("search").OfType<TableNode>());
        Assert.Equal("property/0", Assert.IsType<FieldReferenceNode>(Assert.Single(searchTable.Rows).Cells["value"]).TargetValueId);

        var field = Assert.Single(metadata.ResolveFieldMetadata(["property/0"]));
        Assert.Equal(Confidence.Verified, field.Presentation?.Confidence);
        Assert.Equal("Raw serialized property; the name and storage type are confirmed by its tag.", field.Presentation?.Description);
        Assert.Equal("Not validated on console. The game may reject or overwrite the edited save. Serialized property name and type are verified. Game constraints and effects are unverified; keep a backup.", field.Presentation?.Warning);
        Assert.Equal(1.25, document.GetValue("property/0").AsDouble());
        Assert.Equal(TitleFixture.Settings(), Assert.Single(document.Serialize()).Content.ToArray());

        using var import = new TestSaveImport(TitleFixture.Settings(), "PPSA02199/sdimg_Test/settings.sav");
        using var session = new DocumentSession("settings", import.File, plugin, plugin.Open(import.File));
        var normalized = session.QueryCollection("jedi/settings", new() { Text = "HDRBrightness", Limit = 1 }, null,
            TestContext.Current.CancellationToken);
        var normalizedField = Assert.IsType<FieldNode>(Assert.Single(normalized.Rows).Cells["value"]);
        Assert.Equal("property/0", normalizedField.ValueId);
        Assert.Equal(FieldValue.Of(1.25), normalizedField.Value);
    }

    [Fact]
    public void CanonicalReferenceEditsShareLiveValuesErrorsUndoAndExactBytes()
    {
        var plugin = new UnrealGvasPlugin();
        var original = TitleFixture.Settings();
        using var import = new TestSaveImport(original, "PPSA02199/sdimg_Test/settings.sav");
        var file = import.File;
        using var session = new DocumentSession("settings", file, plugin, plugin.Open(file));

        var first = session.QueryCollection("gvas/properties", new() { Text = "HDRBrightness", Limit = 1 }, null,
            TestContext.Current.CancellationToken);
        var id = Assert.IsType<FieldNode>(Assert.Single(first.Rows).Cells["value"]).ValueId;
        var changed = session.ApplyEdits([new IncomingEdit(id, JsonSerializer.SerializeToElement(1.75))], null,
            TestContext.Current.CancellationToken);
        Assert.True(changed.Ok, string.Join("; ", changed.Errors.Select(error => error.Message)));
        var second = session.QueryCollection("jedi/settings", new() { Text = "HDRBrightness", Limit = 1 }, null,
            TestContext.Current.CancellationToken);
        Assert.Equal(FieldValue.Of(1.75), Assert.IsType<FieldNode>(Assert.Single(second.Rows).Cells["value"]).Value);
        Assert.NotEqual(original, session.Serialize(TestContext.Current.CancellationToken).Single().Content.ToArray());

        var rejected = session.ApplyEdits([new IncomingEdit(id, JsonSerializer.SerializeToElement("not-a-float"))], null,
            TestContext.Current.CancellationToken);
        Assert.False(rejected.Ok);
        Assert.Equal(FieldValue.Of(1.75), session.QueryCollection("gvas/properties", new() { Text = "HDRBrightness", Limit = 1 }, null,
            TestContext.Current.CancellationToken)
            .Rows.Single().Cells["value"] is FieldNode current ? current.Value : FieldValue.Null);
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.Serialize(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SackboyCurrencyUsesTheSameReferenceAcrossRawAndProgressionViews()
    {
        var bytes = SackboyCurrency();
        var plugin = new UnrealGvasPlugin();
        using var import = new TestSaveImport(bytes, "PPSA01288/sdimg_Test/sackboy.sav");
        using var session = new DocumentSession("sackboy", import.File, plugin, plugin.Open(import.File));
        var token = TestContext.Current.CancellationToken;

        var raw = session.QueryCollection("gvas/properties", new() { Text = "CollectabellsStored", Limit = 1 }, null, token);
        var progression = session.QueryCollection("gvas/group/Progression", new() { Limit = 200 }, null, token);
        var rawRow = Assert.Single(raw.Rows);
        var progressionRow = Assert.Single(progression.Rows, row => row.Key == rawRow.Key);
        var rawField = Assert.IsType<FieldNode>(rawRow.Cells["value"]);
        var progressionField = Assert.IsType<FieldNode>(progressionRow.Cells["value"]);
        Assert.Equal(rawField.ValueId, progressionField.ValueId);
        Assert.Equal(FieldValue.Of(17), rawField.Value);
        Assert.False(rawField.Spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, rawField.Confidence);
        Assert.Contains("Checksum coverage is unproven", rawField.Warning, StringComparison.Ordinal);

        var changed = session.ApplyEdits([new IncomingEdit(rawField.ValueId, JsonSerializer.SerializeToElement(23))], null, token);
        Assert.True(changed.Ok, string.Join("; ", changed.Errors.Select(error => error.Message)));
        progression = session.QueryCollection("gvas/group/Progression", new() { Limit = 200 }, null, token);
        progressionRow = Assert.Single(progression.Rows, row => row.Key == rawField.ValueId);
        Assert.Equal(FieldValue.Of(23), Assert.IsType<FieldNode>(progressionRow.Cells["value"]).Value);
        Assert.True(session.Undo().Ok);
        Assert.Equal(FieldValue.Of(17), Assert.IsType<FieldNode>(session.QueryCollection(
            "gvas/properties", new() { Text = "CollectabellsStored", Limit = 1 }, null, token).Rows.Single().Cells["value"]).Value);
    }

    [Fact]
    public void RemovedReferenceIsUnavailableUntilFreshStructuralModelRegistration()
    {
        var plugin = new UnrealGvasPlugin();
        using var document = plugin.Open(MemorySaveSet.SingleFile("nested.sav", GvasFixture.Nested()));
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        const string retained = "property/0/3/0";
        const string removedId = "property/0/3/1";
        Assert.Single(metadata.ResolveFieldMetadata([removedId]));

        var removed = document.InvokeAction("gvas/structure/remove-last/property/0/3", new Dictionary<string, FieldValue>());
        Assert.True(removed.StructureChanged, removed.Message);
        Assert.Empty(metadata.ResolveFieldMetadata([removedId]));
        Assert.Throws<KeyNotFoundException>(() => document.GetValue(removedId));
        _ = document.RestoreMemento(removed.Revert!);
        Assert.Single(metadata.ResolveFieldMetadata([retained]));
        Assert.Single(metadata.ResolveFieldMetadata([removedId]));
    }

    [Fact]
    public void ProtectedAndActorObservationRowsRemainHonest()
    {
        var plugin = new UnrealGvasPlugin();
        using var import = new TestSaveImport(TitleFixture.Scalars("jedi"), "PPSA02199/sdimg_Test/jedi.sav");
        var file = import.File;
        using var session = new DocumentSession("jedi", file, plugin, plugin.Open(file));

        var checksum = session.QueryCollection("gvas/properties", new() { Text = "Crc32", Limit = 1 }, null,
            TestContext.Current.CancellationToken);
        var length = session.QueryCollection("gvas/properties", new() { Text = "UncompressedActorRecordsSize", Limit = 1 }, null,
            TestContext.Current.CancellationToken);
        Assert.True(Assert.IsType<FieldNode>(Assert.Single(checksum.Rows).Cells["value"]).Spec.ReadOnly);
        Assert.True(Assert.IsType<FieldNode>(Assert.Single(length.Rows).Cells["value"]).Spec.ReadOnly);

        var actors = session.QueryCollection("jedi/actor-tags", new() { Limit = 1 }, null, TestContext.Current.CancellationToken);
        Assert.IsType<TextNode>(Assert.Single(actors.Rows).Cells["value"]);
        Assert.Empty(session.Serialize(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void WideStringsKeepBoundedTextFallbackAndFullSearch()
    {
        var large = new string('雪', 4096) + "unique-reference-tail";
        var bytes = GvasFixture.Build(writer =>
        {
            for (var i = 0; i < 200; i++)
                GvasFixture.Property(writer, large, "StrProperty", GvasFixture.Body(body => GvasFixture.String(body, large)));
        });
        var plugin = new UnrealGvasPlugin();
        using var import = new TestSaveImport(bytes, "PPSA00000/sdimg_Test/wide.sav");
        var file = import.File;
        using var session = new DocumentSession("wide", file, plugin, plugin.Open(file));

        var page = session.QueryCollection("gvas/properties", new() { Text = "unique-reference-tail", Limit = 200 }, null,
            TestContext.Current.CancellationToken);

        Assert.Equal(200, page.Rows.Count);
        Assert.All(page.Rows, row => Assert.IsType<TextNode>(row.Cells["value"]));
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(page).Length < 2 * 1024 * 1024);
        Assert.Empty(session.Serialize(TestContext.Current.CancellationToken));
    }

    private static byte[] SackboyCurrency() => GvasFixture.Build(writer =>
    {
        GvasFixture.Property(writer, "CollectabellSaveData", "StructProperty", GvasFixture.Body(body =>
        {
            GvasFixture.Property(body, "CollectabellsStored", "IntProperty", GvasFixture.Body(value => value.Write(17)));
            GvasFixture.String(body, "None");
        }), "CollectabellSaveData");
        GvasFixture.Property(writer, "Checksum", "UInt32Property", [0x78, 0x56, 0x34, 0x12]);
    }, minor: 25, patch: 1, packageVersion: 518, saveClass: "/Script/GingerBread.GingerbreadSaveUser");
}
