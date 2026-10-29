using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class GroupHistoryTests
{
    private const string CharacterPath = "PPSA16064/sdimg_Character0/hero.sav";
    private const string GlobalPath = "PPSA16064/sdimg_GlobalSaveDataDefault/global.sav";
    private const string InventoryCollection = "character/character/inventory";
    private const string Hero = """
        {"SerializeMeta":{"HardFormat":"FCharacterSaveV1","SoftVersion":2,"FormatHash":3777917472},
         "CharacterSaveV1":{"MetaData":{"IsOnline":false,"Level":1},"Inventory":{"Entries":[
         {"ItemData":{"TypeTag":"SW.Item.Sword"},"StackCount":1,"future":"\u0041"},
         {"ItemData":{"TypeTag":"SW.Item.Sword"},"StackCount":2,"future":-0.000E+00}]}}}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedMemberAndStructuralHistoryRestoresExactBytesAndStableRows(bool xor)
    {
        var character = Encoding.UTF8.GetBytes(Hero);
        var global = XorJsonTests.Encode("{\"blobs\":[],\"volume\":50,\"future\":1.2300E+02}", xor);
        var plugin = new XorJsonPlugin();
        using var scratch = new WorkspaceScratch();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions
        {
            DataRoot = scratch.Root,
        });
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = zip.CreateEntry(CharacterPath).Open()) entry.Write(character);
            using (var entry = zip.CreateEntry(GlobalPath).Open()) entry.Write(global);
        }
        var workspace = store.Create("group-history", [new ImportSource("group.zip", () => new MemoryStream(archive.ToArray()))]);
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.RelativePath == CharacterPath);
        var proposal = Assert.Single(workspace.ProposeGroups(anchor.Id, plugin.Info.Id));
        Assert.Empty(proposal.Errors);
        var session = workspace.OpenGroup(proposal.Token).Session;
        var fields = ViewTraversal.Nodes(session).OfType<FieldNode>().ToArray();
        var level = fields.Single(field => field.Label == "Level");
        var volume = fields.Single(field => field.Label == "volume");
        var online = fields.Single(field => field.Label == "IsOnline");
        var rows = session.QueryCollection(InventoryCollection, new()).Rows;
        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0].Key, rows[1].Key);
        var survivor = Assert.IsType<FieldNode>(rows[1].Cells["stack"]);
        var original = Bytes(workspace);
        Assert.Equal(character, original[CharacterPath]);
        Assert.Equal(global, original[GlobalPath]);

        var batch = session.ApplyEdits([Edit(level.ValueId, FieldValue.ExactNumber("2")), Edit(volume.ValueId, FieldValue.ExactNumber("49"))]);
        Assert.True(batch.Ok, Errors(batch));
        Assert.Contains(batch.Warnings, warning => warning.Contains("transaction", StringComparison.OrdinalIgnoreCase));
        var afterBatch = Bytes(workspace);
        Assert.Equal(Encoding.UTF8.GetBytes(Hero.Replace("\"Level\":1", "\"Level\":2", StringComparison.Ordinal)), afterBatch[CharacterPath]);
        Assert.Equal(XorJsonTests.Encode("{\"blobs\":[],\"volume\":49,\"future\":1.2300E+02}", xor), afterBatch[GlobalPath]);

        var oldRevision = session.State.Revision;
        var removeId = "character/json/remove/" + rows[0].Key.Split('/')[2];
        var removal = session.InvokeAction(removeId, new Dictionary<string, JsonElement>(), oldRevision);
        Assert.True(removal.Ok, Errors(removal));
        Assert.True(removal.StructureChanged);
        Assert.Equal(rows[1].Key, Assert.Single(session.QueryCollection(InventoryCollection, new()).Rows).Key);
        Assert.True(session.ApplyEdits([Edit(volume.ValueId, FieldValue.ExactNumber("48"))], oldRevision).Stale);
        var afterRemove = Bytes(workspace);

        Assert.True(session.ApplyEdits([Edit(survivor.ValueId, FieldValue.ExactNumber("3"))]).Ok);
        var afterScalar = Bytes(workspace);
        var beforeFailure = session.State;
        // The global member can accept its edit, but the character's mode invariant rejects
        // the later member. Neither file nor the preceding undo history may be published.
        var rejected = session.ApplyEdits([
            Edit(volume.ValueId, FieldValue.ExactNumber("10")),
            Edit(level.ValueId, FieldValue.ExactNumber("3")),
            Edit(online.ValueId, FieldValue.Of(true)),
        ]);
        Assert.False(rejected.Ok);
        Assert.Equal(beforeFailure, session.State);
        AssertBytes(afterScalar, workspace);

        for (var cycle = 0; cycle < 2; cycle++)
        {
            Assert.True(session.Undo().Ok);
            AssertBytes(afterRemove, workspace);
            Assert.True(session.Undo().Ok);
            AssertBytes(afterBatch, workspace);
            Assert.Equal(rows.Select(row => row.Key), session.QueryCollection(InventoryCollection, new()).Rows.Select(row => row.Key));
            Assert.True(session.Undo().Ok);
            AssertBytes(original, workspace);
            Assert.False(session.State.IsDirty);
            Assert.False(session.State.CanUndo);
            Assert.True(session.Redo().Ok);
            Assert.True(session.Redo().Ok);
            Assert.True(session.Redo().Ok);
            AssertBytes(afterScalar, workspace);
            Assert.Equal(rows[1].Key, Assert.Single(session.QueryCollection(InventoryCollection, new()).Rows).Key);
        }
    }

    private static IncomingEdit Edit(string id, FieldValue value) => new(id, JsonSerializer.SerializeToElement(value));
    private static string Errors(SessionEditResult result) => string.Join("; ", result.Errors.Select(error => error.Message));
    private static Dictionary<string, byte[]> Bytes(Workspace workspace)
    {
        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.Everything });
        using var output = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, output);
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var stream = entry.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return bytes.ToArray();
        }, StringComparer.Ordinal);
    }

    private static void AssertBytes(IReadOnlyDictionary<string, byte[]> expected, Workspace workspace)
    {
        var actual = Bytes(workspace);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, actual[path]);
    }

}
