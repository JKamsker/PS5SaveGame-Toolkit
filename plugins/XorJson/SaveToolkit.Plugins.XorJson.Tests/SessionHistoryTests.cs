using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class SessionHistoryTests
{
    [Fact]
    public void CoreReplaysScalarAndStructuralHistoryAndRejectsStaleReferences()
    {
        var original = XorJsonTests.Encode("{\"rows\":[{\"text\":\"\\u0041\",\"n\":-0},{\"text\":\"other\",\"n\":1.00E+02}]}");
        var root = Path.Combine(Path.GetTempPath(), "SaveToolkit.XorJson.Tests", Guid.NewGuid().ToString("N"));
        using var store = new WorkspaceStore(new PluginCatalog([new XorJsonPlugin()]), new WorkspaceStoreOptions { DataRoot = root });
        using var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("PPSA99998/sdimg_SAVE/synthetic.sav").Open();
            entry.Write(original);
        }
        var workspace = store.Create("xorjson", [new ImportSource("fixture.zip", () => new MemoryStream(zipBytes.ToArray()))]);
        var file = workspace.Set.AllDiskFiles.Single();
        var plugin = new XorJsonPlugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("xorjson-history", file, plugin, document);
        var nodes = Expand(session);
        var first = nodes.OfType<FieldNode>().Single(f => f.Label == "text" && f.Value.AsString() == "A");
        var survivor = nodes.OfType<FieldNode>().Single(f => f.Label == "n" && f.Value.AsExactNumber() == "1.00E+02");
        Assert.True(session.ApplyEdits([Incoming(first.ValueId, FieldValue.Of("edited"))]).Ok);
        var row = nodes.OfType<SectionNode>().First(n => n.Title == "[0]" && n.LazyId is not null);
        var actionId = "json/remove/" + row.LazyId!.Split('/')[1];
        var beforeRevision = session.State.Revision;
        var remove = session.InvokeAction(actionId, new Dictionary<string, JsonElement>());
        Assert.True(remove.Ok);
        Assert.True(remove.StructureChanged);
        Assert.True(remove.State.CanUndo);
        Assert.True(session.ApplyEdits([Incoming(first.ValueId, FieldValue.Of("retired"))]).Stale);
        Assert.True(session.ApplyEdits([Incoming(survivor.ValueId, FieldValue.ExactNumber("2"))], expectedRevision: beforeRevision).Stale);
        Assert.True(session.ApplyEdits([Incoming(survivor.ValueId, FieldValue.ExactNumber("2.000E+02"))]).Ok);
        var edited = document.Serialize()[0].Content.ToArray();
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.Equal(original, document.Serialize()[0].Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.True(session.Redo().Ok);
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, document.Serialize()[0].Content.ToArray());
    }

    private static IncomingEdit Incoming(string id, FieldValue value) => new(id, JsonSerializer.SerializeToElement(value));

    private static IReadOnlyList<ViewNode> Expand(DocumentSession session)
    {
        var nodes = new List<ViewNode>();
        Visit(session.BuildView());
        return nodes;
        void Visit(ViewNode node)
        {
            nodes.Add(node);
            if (node is TabsNode tabs) foreach (var child in tabs.Tabs.SelectMany(t => t.Children)) Visit(child);
            if (node is SectionNode section)
                foreach (var child in section.LazyId is { } id ? session.Expand(id) : section.Children) Visit(child);
        }
    }
}
