using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseRestoreImportedSampleTests
{
    private const string ActionId = "inventory/restore-imported";
    private const string QuantityId = "inventory/24/quantity";
    private static readonly IReadOnlyDictionary<string, JsonElement> NoParameters = new Dictionary<string, JsonElement>();

    [Fact]
    public void SampleRestore_PreviewCanBeDismissed_ApplyUndoRedoAreOneGroup_AndReimportResetsBaseline()
    {
        var plugin = new TalesOfArisePlugin();
        var files = AriseSampleTests.GameplayFiles().Where(file => file.FileName == "SaveData_002.sav").Cast<DiskSaveFile>().ToArray();
        Assert.Equal(2, files.Length);
        foreach (var file in files)
        {
            using var document = plugin.Open(file);
            using var session = new DocumentSession("arise-restore", file, plugin, document);
            var root = Assert.IsType<TabsNode>(session.BuildView());
            var action = Assert.Single(root.Tabs.Single(tab => tab.Title == "Inventory").Children.OfType<ActionNode>(), node => node.ActionId == ActionId);
            Assert.True(action.SupportsPlanning);
            Assert.Empty(action.Parameters);
            Assert.Equal(Tone.Warning, action.Tone);
            _ = session.Expand("inventory");
            _ = session.QueryCollection("inventory", new() { Text = "Apple Gel" }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(session.ApplyEdits([new(QuantityId, JsonSerializer.SerializeToElement("0"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            var reduced = Bytes(document);
            var revision = session.State.Revision;
            var scope = new ActionScope([QuantityId]);
            var dismissed = session.PlanAction(ActionId, NoParameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(dismissed.PlanId);
            Assert.Equal(new PlannedChange(QuantityId, "Apple Gel · quantity (raw ID 0x0001)", FieldValue.Of(0L), FieldValue.Of(1L)), Assert.Single(dismissed.Plan.Changes));
            Assert.Equal(revision, session.State.Revision);
            Assert.Equal(reduced, Bytes(document));
            Assert.Equal(0, document.GetValue(QuantityId).AsInt64());

            var preview = session.PlanAction(ActionId, NoParameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
            Assert.Equal(file.ReadAllBytes(), Bytes(document));
            Assert.True(session.Undo().Ok);
            Assert.Equal(reduced, Bytes(document));
            Assert.True(session.Redo().Ok);
            Assert.Equal(file.ReadAllBytes(), Bytes(document));
            Assert.True(session.Undo().Ok);
            Assert.True(session.Undo().Ok);
            Assert.False(session.State.CanUndo); // The reduction and restoration were exactly two history groups.

            using var reimported = plugin.Open(MemorySaveSet.SingleFile(file.FileName, reduced, AriseTestContainer.TitleId));
            _ = reimported.ExpandNode("inventory");
            var fresh = Assert.IsAssignableFrom<IActionPlanningDocument>(reimported).PlanAction(ActionId,
                new Dictionary<string, FieldValue>(), new());
            Assert.Empty(fresh.Errors);
            Assert.Empty(fresh.Changes);
            Assert.Equal(reduced, Bytes(reimported));
        }
    }

    [Fact]
    public void RestoreQueryScope_ResolvesUnloadedRows_AndHonoursExclusions()
    {
        var plugin = new TalesOfArisePlugin();
        var file = Assert.IsType<DiskSaveFile>(AriseSampleTests.GameplayFiles().First(file => file.FileName == "SaveData_002.sav"));
        using var document = plugin.Open(file);
        using var session = new DocumentSession("arise-restore-query", file, plugin, document);
        _ = session.BuildView();
        _ = session.QueryCollection("inventory", new() { Text = "Apple Gel" }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(session.ApplyEdits([new(QuantityId, JsonSerializer.SerializeToElement("0"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var scope = new ActionScope { QueryScope = new("inventory", new() { Text = "Apple Gel" }, session.State.Revision) };
        var excluded = session.PlanAction(ActionId, NoParameters, scope with
        {
            QueryScope = scope.QueryScope with { ExcludedRowKeys = [QuantityId] },
        }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(excluded.PlanId);
        Assert.Empty(excluded.Plan.Changes);
        var preview = session.PlanAction(ActionId, NoParameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(new[] { QuantityId }, preview.Scope.RowKeys);
        Assert.Single(preview.Plan.Changes);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(file.ReadAllBytes(), Bytes(document));
    }

    [Fact]
    public void Restore_ReportsExposedPackedWordAsDerived_AndUndoesOnlyTheWritableQuantity()
    {
        var plugin = new TalesOfArisePlugin();
        var file = Assert.IsType<DiskSaveFile>(AriseSampleTests.GameplayFiles().First(file => file.FileName == "SaveData_002.sav"));
        using var document = plugin.Open(file);
        using var session = new DocumentSession("arise-restore-alias", file, plugin, document);
        _ = session.BuildView();
        _ = session.Expand("inventory");
        _ = session.QueryCollection("inventory", new() { Text = "Apple Gel" }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var save = AriseFormat.Decode(file.ReadAllBytes());
        var index = save.Sections.ToList().FindIndex(section => section.Name == "SaveData_ItemManager");
        var rawId = $"raw/{save.Sections[index].Offset + 24 * AriseTestContainer.ItemRecordSize:X}";
        _ = session.Expand($"section/{index}");
        _ = session.Expand($"page/{index}/0");
        var importedWord = document.GetValue(rawId);
        Assert.True(session.ApplyEdits([new(QuantityId, JsonSerializer.SerializeToElement("0"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var reduced = Bytes(document);
        var preview = session.PlanAction(ActionId, NoParameters, new ActionScope([QuantityId]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        var alias = Assert.Single(preview.Plan.Changes, change => change.Derived);
        Assert.Equal(rawId, alias.ValueId);
        Assert.Equal(importedWord, alias.NewValue);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(importedWord, document.GetValue(rawId));
        Assert.Equal(file.ReadAllBytes(), Bytes(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, Bytes(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(file.ReadAllBytes(), Bytes(document));
    }

    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
}
