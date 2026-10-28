using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class SavedQuantityWritingTests
{
    [Fact]
    public void QueryPageQuantityEditsUseCoreHistoryAndKeepTheSameIdentityThroughUndoRedo()
    {
        var original = PackageFixture.Create(payloads: [("Globals.lsf", ResourceFixture.Create(itemQuantity: true))]);
        var root = Path.Combine(Path.GetTempPath(), "SaveToolkit.Bg3.Tests", Guid.NewGuid().ToString("N"));
        var plugin = new BaldursGate3Plugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = root });
        using var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("PPSA18463/sdimg_SAVE/test.lsv").Open();
            entry.Write(original);
        }
        var workspace = store.Create("bg3", [new ImportSource("fixture.zip", () => new MemoryStream(zipBytes.ToArray()))]);
        var file = workspace.Set.AllDiskFiles.Single();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("bg3-quantity", file, plugin, document);
        _ = session.BuildView();
        var field = PageQuantity();
        Assert.True(session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement(6))]).Ok);
        var edited = Assert.Single(session.Serialize()).Content.ToArray();
        Assert.Equal(6, PageQuantity().Value.AsInt32());
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(7, PageQuantity().Value.AsInt32());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(session.Serialize()).Content.ToArray());
        Assert.Equal(field.ValueId, PageQuantity().ValueId);
        Assert.False(session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement(8))]).Ok);

        FieldNode PageQuantity() => Assert.IsType<FieldNode>(Assert.Single(session.QueryCollection("bg3.saved-items",
            new QueryableCollectionQuery { Limit = 1, SortColumn = "quantity" }).Rows).Cells["quantity"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitQuantityUsesCanonicalBindingPersistsAndUndoesWithoutChangingOtherBytes(bool packaged)
    {
        var lsf = ResourceFixture.Create(itemQuantity: true);
        var original = packaged ? PackageFixture.Create(method: 2, payloads: [("Globals.lsf", lsf), ("unknown.bin", [9, 8, 7])]) : lsf;
        var file = MemorySaveSet.SingleFile(packaged ? "test.lsv" : "Globals.lsf", original, "PPSA18463");
        using var document = Assert.IsType<BaldursGate3Document>(new BaldursGate3Plugin().Open(file));
        _ = document.BuildView();
        var field = Quantity(document);
        Assert.Equal("resource/0/attribute/0", field.ValueId);
        Assert.Equal(Confidence.Unknown, field.Confidence);
        Assert.False(field.Spec.ReadOnly);
        Assert.Contains("game may reject", field.Warning, StringComparison.Ordinal);
        Assert.Contains("Not validated on console", field.Description, StringComparison.Ordinal);
        foreach (var invalid in new[] { 0L, -1L, 8L, long.MaxValue })
            Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(invalid))]).Succeeded);
        _ = document.ExpandNode("attributes/0/0/0/1");
        Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(6L)),
            new ValueEdit("resource/0/attribute/1", FieldValue.Of("bad\0text"))]).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        var outcome = document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(6L))]);
        Assert.True(outcome.Succeeded);
        Assert.Equal(6, Quantity(document).Value.AsInt32()); // paged/query binding reads current state
        _ = document.ExpandNode("attributes/0/1/0/2"); // rebinding in raw explorer must retain constraints
        Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(8L))]).Succeeded);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        var changedLsf = packaged ? ExtractGlobals(edited) : edited;
        if (packaged)
        {
            var beforePackage = LspkPackage.Read(original);
            var afterPackage = LspkPackage.Read(edited);
            var beforeEntry = beforePackage.Entries.Single(entry => entry.Name == "unknown.bin");
            var afterEntry = afterPackage.Entries.Single(entry => entry.Name == "unknown.bin");
            Assert.True(original.AsSpan(beforeEntry.Offset, beforeEntry.StoredSize).SequenceEqual(edited.AsSpan(afterEntry.Offset, afterEntry.StoredSize)));
        }
        var before = LsfBinary.Read(lsf).Data(LsfLayout.ValuesPair);
        var after = LsfBinary.Read(changedLsf).Data(LsfLayout.ValuesPair);
        var diff = ByteDiffer.Compare(before, after, options: new ByteDiffOptions { MergeGap = 0, ContextBytes = 0 });
        var range = Assert.Single(diff.Ranges);
        Assert.Equal(0, range.Offset);
        Assert.Equal(1, range.Length);
        var oldBinary = LsfBinary.Read(lsf);
        var newBinary = LsfBinary.Read(changedLsf);
        foreach (var section in new[] { LsfLayout.StringsPair, LsfLayout.KeysPair, LsfLayout.NodesPair, LsfLayout.AttributesPair })
            Assert.Equal(oldBinary.Data(section), newBinary.Data(section));
        using var reopened = Assert.IsType<BaldursGate3Document>(new BaldursGate3Plugin().Open(
            MemorySaveSet.SingleFile(file.FileName, edited, "PPSA18463")));
        _ = reopened.BuildView();
        Assert.Equal(6, Quantity(reopened).Value.AsInt32());
        Assert.True(document.ApplyEdits(outcome.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits(outcome.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray()).Succeeded);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ZeroNegativeDuplicateAndConflictingAliasCandidatesNeverBecomeQuantityControls()
    {
        foreach (var value in new[] { 0, -1 })
        {
            var attribute = new ResourceAttribute("Quantity", LarianAttributeType.Int, BitConverter.GetBytes(value));
            Assert.Null(Policy([attribute]).Find(0, 1));
        }
        var positive = new ResourceAttribute("Quantity", LarianAttributeType.Int, BitConverter.GetBytes(7));
        Assert.Null(Policy([positive, positive]).Find(0, 1));
        Assert.Null(Policy([positive, positive with { Name = "Amount" }]).Find(0, 1));
        Assert.Null(Policy([positive with { Type = LarianAttributeType.String, Data = "7\0"u8.ToArray() }]).Find(0, 1));
    }

    [Fact]
    public void SampleHasNoExplicitQuantityControlsAndNewAgeAndStoryRemainUntouched()
    {
        var file = SampleSaves.Load("PPSA18463").AllDiskFiles.First(file => file.FileName == "AutoSave_0.lsv" && !file.Directory.IsBackup);
        using var document = Assert.IsType<BaldursGate3Document>(new BaldursGate3Plugin().Open(file));
        _ = document.BuildView();
        var page = document.Query("bg3.saved-items", new QueryableCollectionQuery { Limit = 128 });
        Assert.Equal(761, page.TotalCount);
        var examined = 0;
        for (var offset = 0; offset < page.TotalCount; offset += 128)
        {
            var rows = document.Query("bg3.saved-items", new QueryableCollectionQuery { Offset = offset, Limit = 128 }).Rows;
            Assert.All(rows, row => Assert.IsType<TextNode>(row.Cells["quantity"]));
            examined += rows.Count;
        }
        Assert.Equal(761, examined);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static SavedQuantityEdits Policy(ResourceAttribute[] attributes)
    {
        var root = new ResourceNode("Items", -1);
        var item = new ResourceNode("Item", 0);
        item.Attributes.AddRange(Enumerable.Range(0, attributes.Length));
        return new SavedQuantityEdits([new DocumentEntry("Globals.lsf", [], new LarianResource("synthetic", [root, item], attributes, [0]))]);
    }

    private static FieldNode Quantity(BaldursGate3Document document) => Assert.IsType<FieldNode>(Assert.Single(document.Query(
        "bg3.saved-items", new QueryableCollectionQuery { Limit = 1 }).Rows).Cells["quantity"]);

    private static byte[] ExtractGlobals(byte[] bytes)
    {
        var package = LspkPackage.Read(bytes);
        return package.Extract(package.Entries.Single(entry => entry.Name == "Globals.lsf"));
    }
}
