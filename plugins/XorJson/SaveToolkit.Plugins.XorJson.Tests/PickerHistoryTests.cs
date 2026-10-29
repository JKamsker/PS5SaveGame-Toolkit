using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class PickerHistoryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExistingValuePickerRejectsUnknownIncomingIdsAndPreservesBytesAcrossReopenAndHistory(bool xor, bool unknownOriginal)
    {
        var originalId = unknownOriginal ? "SW.Item.Future" : "SW.Item.Sword";
        var original = "{\r\n \"TypeTag\":\"" + (unknownOriginal ? originalId : "SW.Item.\\u0053word") +
            "\", \"unknown\":-0.0000E+0002, \"extra\":{\"duplicate\":1,\"duplicate\":2} }\t";
        var bytes = XorJsonTests.Encode(original, xor);
        using var scratch = new WorkspaceScratch();
        var plugin = new XorJsonPlugin();
        var packs = new ReferencePackStore([plugin]);
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = scratch.Root });
        using var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("PPSA16064/sdimg_SAVE/synthetic.sav").Open();
            entry.Write(bytes);
        }
        var workspace = store.Create("picker", [new ImportSource("fixture.zip", () => new MemoryStream(zipBytes.ToArray()))]);
        var file = workspace.Set.AllDiskFiles.Single();
        var references = packs.Pin(plugin, "PPSA16064");
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("picker", file, plugin, document, references: references);
        var field = XorJsonTests.Fields(document).Single(field => field.Label == "TypeTag");
        _ = ViewTraversal.Nodes(session);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        var candidates = session.SearchReferences(field.ValueId, new ReferenceQuery { Text = "Bow" }, revision: 0);
        Assert.Contains(candidates.Items, candidate => candidate.RawId == "SW.Item.Bow");
        Assert.All(candidates.Items, candidate => Assert.Equal(GameAssetTests.OwnerDecision, candidate.Dataset!.Attribution.License));
        Assert.Equal(!unknownOriginal, session.FindReference(field.ValueId, originalId).Eligibility.Selectable);
        Assert.False(session.ApplyEdits([Incoming(field.ValueId, "SW.Item.NotInCatalog")]).Ok);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        var edited = session.ApplyEdits([Incoming(field.ValueId, "SW.Item.Bow")]);
        Assert.True(edited.Ok);
        Assert.True(edited.StructureChanged); // Refresh name/provenance and picker metadata.
        Assert.Contains("Bow", XorJsonTests.Fields(document).Single(field => field.Label == "TypeTag").Description!);
        var expected = XorJsonTests.Encode(original.Replace(unknownOriginal ? originalId : "SW.Item.\\u0053word", "SW.Item.Bow", StringComparison.Ordinal), xor);
        Assert.Equal(expected, document.Serialize()[0].Content.ToArray());
        using var reopened = XorJsonTests.Open(expected);
        Assert.Equal("SW.Item.Bow", XorJsonTests.Fields(reopened).Single(field => field.Label == "TypeTag").Value.AsString());
        var undo = session.Undo();
        Assert.True(undo.Ok);
        Assert.True(undo.StructureChanged);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        Assert.False(session.State.IsDirty);
        var redo = session.Redo();
        Assert.True(redo.Ok);
        Assert.True(redo.StructureChanged);
        Assert.Equal(expected, document.Serialize()[0].Content.ToArray());
    }

    private static IncomingEdit Incoming(string id, string value) => new(id, JsonSerializer.SerializeToElement(value));

}
