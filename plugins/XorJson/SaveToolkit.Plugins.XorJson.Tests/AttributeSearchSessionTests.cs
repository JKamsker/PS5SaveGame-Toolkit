using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class AttributeSearchSessionTests
{
    private const string CharacterPath = "PPSA16064/sdimg_Character0/hero.sav";
    private const string GlobalPath = "PPSA16064/sdimg_GlobalSaveDataDefault/global.sav";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AliasQueryScopeSelectsUnloadedRowsAndKeepsExactHistory(bool grouped, bool xor)
    {
        var attributes = Enumerable.Range(0, AttributeSearchData.UnmatchedRows).Select(index => ($"Unknown{index:D3}", index))
            .Concat(new[] { ("SpringStone", 1), ("SpringStone", 2), ("SpringStone", 3), ("Emeralds", 4) }).ToArray();
        var original = AttributeSearchData.Hero(attributes);
        var global = XorJsonTests.Encode("{\"blobs\":[],\"volume\":50,\"future\":-0.00E+02}", xor);
        var plugin = new XorJsonPlugin();
        using var scratch = new WorkspaceScratch();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = scratch.Root });
        var workspace = Import(store, original, grouped ? global : null);
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.RelativePath == CharacterPath);
        var session = grouped
            ? workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(anchor.Id, plugin.Info.Id)).Token).Session
            : workspace.Open(anchor.Id, plugin.Info.Id);
        _ = session.BuildView();
        var prefix = grouped ? "character/" : "";
        var collection = prefix + AttributeSearchData.CollectionId;
        var first = session.QueryCollection(collection, new() { Text = "ECHO shards", Limit = 1 },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, first.FilteredCount);
        var firstRow = Assert.Single(first.Rows);
        var firstValue = Assert.IsType<FieldNode>(firstRow.Cells["value"]);
        var excluded = Assert.Single(session.QueryCollection(collection, new() { Text = "Echo shards", Offset = 1, Limit = 1 },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        AssertBytes(workspace, original, grouped ? global : null);

        var scope = new ActionScope
        {
            QueryScope = new ActionQueryScope(collection, new QueryableCollectionQuery
            {
                Text = "eChO sHaRdS", Offset = 200, Limit = 1, SortColumn = "id", Descending = true,
            }, session.State.Revision) { ExcludedRowKeys = [excluded.Key] },
        };
        var preview = session.PlanAction(prefix + "character/set-attribute", new Dictionary<string, JsonElement>
        {
            ["id"] = JsonSerializer.SerializeToElement("SpringStone"),
            ["value"] = JsonSerializer.SerializeToElement(9),
        }, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        var selected = Assert.IsAssignableFrom<IReadOnlyList<string>>(preview.Scope.RowKeys);
        Assert.Equal(2, selected.Count);
        Assert.Contains(firstRow.Key, selected);
        Assert.DoesNotContain(excluded.Key, selected);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.Contains(preview.Plan.Changes, change => change.ValueId == firstValue.ValueId);
        Assert.All(preview.Plan.Changes, change => Assert.Equal(FieldValue.ExactNumber("9"), change.NewValue));
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        AssertBytes(workspace, original, grouped ? global : null);

        var apply = session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(apply.Ok, string.Join("; ", apply.Errors.Select(error => error.Message)));
        attributes[AttributeSearchData.UnmatchedRows] = ("SpringStone", 9);
        attributes[AttributeSearchData.UnmatchedRows + 2] = ("SpringStone", 9);
        var expected = AttributeSearchData.Hero(attributes);
        AssertBytes(workspace, expected, grouped ? global : null);
        var changed = session.QueryCollection(collection, new() { Text = "Echo shards" },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["9", "2", "9"], changed.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["value"]).Value.AsExactNumber()));
        Assert.Equal(firstRow.Key, changed.Rows[0].Key);
        Assert.Equal(excluded.Key, changed.Rows[1].Key);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo); // The entire query action is one history step.
        AssertBytes(workspace, original, grouped ? global : null);
        var restored = session.QueryCollection(collection, new() { Text = "Echo shards" },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(changed.Rows.Select(row => row.Key), restored.Rows.Select(row => row.Key));
        Assert.True(session.Redo().Ok);
        AssertBytes(workspace, expected, grouped ? global : null);
    }

    private static Workspace Import(WorkspaceStore store, byte[] character, byte[]? global)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = zip.CreateEntry(CharacterPath).Open()) entry.Write(character);
            if (global is not null)
            {
                using var entry = zip.CreateEntry(GlobalPath).Open();
                entry.Write(global);
            }
        }
        var archive = bytes.ToArray();
        return store.Create("attribute-search", [new ImportSource("synthetic.zip", () => new MemoryStream(archive))]);
    }

    private static void AssertBytes(Workspace workspace, byte[] character, byte[]? global)
    {
        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.Everything });
        using var bytes = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, bytes);
        bytes.Position = 0;
        using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
        Assert.Equal(global is null ? 1 : 2, zip.Entries.Count);
        AssertEntry(zip, CharacterPath, character);
        if (global is not null) AssertEntry(zip, GlobalPath, global);
    }

    private static void AssertEntry(ZipArchive zip, string path, byte[] expected)
    {
        using var entry = Assert.Single(zip.Entries, entry => entry.FullName == path).Open();
        using var bytes = new MemoryStream();
        entry.CopyTo(bytes);
        Assert.Equal(expected, bytes.ToArray());
    }
}
