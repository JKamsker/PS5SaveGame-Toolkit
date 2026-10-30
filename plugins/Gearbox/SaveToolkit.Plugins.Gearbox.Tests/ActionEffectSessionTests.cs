using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ActionEffectSessionTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ProductionStandaloneImportHasExactProjectionOneUndoUnitAndFullByteReopen(bool wonderlands, bool profile)
    {
        using var original = ActionEffectData.Document(wonderlands, profile);
        var set = new MemorySaveSet().With(ActionEffectData.Title(wonderlands), "STANDALONE", new Dictionary<string, byte[]>
        { [profile ? "profile" : "1"] = original.Serialize().Single().Content.ToArray() });
        using var workspace = MetadataR6Data.Workspace(set);
        var file = workspace.Set.AllDiskFiles.Single();
        var plugin = new GearboxPlugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("effects", file, plugin, document);
        _ = session.BuildView();
        var before = document.Serialize().Single().Content.ToArray();
        var parameters = new Dictionary<string, JsonElement> { ["code"] = JsonSerializer.SerializeToElement(Convert.ToBase64String(ActionEffectData.Serial(wonderlands))) };
        var preview = session.PlanAction("items/import", parameters, null, null, Cancellation);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.Equal(1, preview.Plan.Changes.Count(change => change.Effect is not null));
        Assert.Equal(FieldValue.Of(2), preview.Plan.Changes[0].OldValue);
        Assert.Equal(FieldValue.Of(3), preview.Plan.Changes[0].NewValue);
        Assert.False(session.State.CanUndo);
        var applied = session.ApplyActionPlan(preview.PlanId!, preview.Revision, Cancellation);
        Assert.True(applied.Ok, string.Join("; ", applied.Errors));
        AssertProjection(preview.Plan, applied);
        var after = session.Serialize(Cancellation).Single();
        Assert.NotEqual(before, after.Content.ToArray());
        using var reopened = (GearboxDocument)plugin.Open(GearboxTestData.Overlay(file, after.Content));
        Assert.Equal(after.Content.ToArray(), reopened.Serialize().Single().Content.ToArray());
        Assert.Equal(profile ? 4 : 3, reopened.Inventory.Entries.Count());
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.True(session.State.CanRedo);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(after.Content.ToArray(), session.Serialize(Cancellation).Single().Content.ToArray());
        Assert.False(session.State.CanRedo);
        Assert.False(session.ApplyActionPlan(preview.PlanId!, preview.Revision, Cancellation).Ok);
        Assert.Equal(after.Content.ToArray(), session.Serialize(Cancellation).Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false, "transfer/deposit", "character/inventory_items/item-0")]
    [InlineData(true, "transfer/deposit", "character/inventory_items/item-0")]
    [InlineData(false, "transfer/withdraw", "profile/bank_inventory_list/item-0")]
    [InlineData(true, "transfer/withdraw", "profile/bank_inventory_list/item-0")]
    [InlineData(false, "transfer/claim", "profile/lost_loot_inventory_list/item-2")]
    [InlineData(true, "transfer/claim", "profile/lost_loot_inventory_list/item-2")]
    [InlineData(false, "character/items/import", "")]
    [InlineData(true, "profile/items/import", "")]
    public void ProductionGroupEffectsKeepEveryRowOutputsAndCurrentCandidateBindings(bool wonderlands, string action, string key)
    {
        var set = ActionEffectData.Set(wonderlands);
        using var sourceWorkspace = MetadataR6Data.Workspace(set);
        var observer = new SelectionGroupObserver();
        using var workspace = new Workspace("effects", "Effects", sourceWorkspace.Set,
            new PluginCatalog([observer]), new ReferencePackStore([observer]));
        var file = workspace.Set.AllDiskFiles.Single(file => file.FileName == "1");
        var opened = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(file.Id, "gearbox")).Token);
        var session = opened.Session;
        _ = session.BuildView();
        var parameters = action.EndsWith("items/import", StringComparison.Ordinal)
            ? new Dictionary<string, JsonElement> { ["code"] = JsonSerializer.SerializeToElement(Convert.ToBase64String(ActionEffectData.Serial(wonderlands))) }
            : [];
        var scope = key.Length == 0 ? new ActionScope() : new([key]);
        var before = ActionEffectData.Bytes(observer.Current);
        var state = session.State;
        var preview = session.PlanAction(action, parameters, scope, null, Cancellation);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(state, session.State);
        ActionEffectData.AssertBytes(before, observer.Current);
        AssertOrderedEffects(preview.Plan, action);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancelled.Token));
        Assert.Equal(state, session.State);
        ActionEffectData.AssertBytes(before, observer.Current);
        var applied = session.ApplyActionPlan(preview.PlanId!, preview.Revision, Cancellation);
        Assert.True(applied.Ok, string.Join("; ", applied.Errors.Select(error => error.Message)));
        AssertProjection(preview.Plan, applied);
        var after = ActionEffectData.Bytes(observer.Current);
        Assert.Equal(2, after.Count);
        Assert.Equal(2, session.Serialize(Cancellation).Select(output => output.RelativePath).Distinct(StringComparer.Ordinal).Count());
        var export = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.Everything }, Cancellation);
        Assert.Equal(2, export.Overrides.Count);
        foreach (var output in session.Serialize(Cancellation))
        {
            var source = ((ISaveSet)set).FindFile(output.RelativePath)!;
            using var reopened = new GearboxPlugin().Open(GearboxTestData.Overlay(source, output.Content));
            Assert.Equal(output.Content.ToArray(), reopened.Serialize().Single().Content.ToArray());
        }
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.False(session.State.IsDirty);
        ActionEffectData.AssertBytes(before, observer.Current);
        Assert.True(session.Redo().Ok);
        ActionEffectData.AssertBytes(after, observer.Current);
        // Rebinding must target the published candidate after both restores, including newly moved IDs.
        var collection = action == "transfer/deposit" || action.StartsWith("profile/", StringComparison.Ordinal)
            ? "profile/bank_inventory_list" : "character/inventory_items";
        var page = session.QueryCollection(collection, new() { Limit = 20 }, null, Cancellation);
        Assert.NotEmpty(page.Rows);
        var role = collection[..collection.IndexOf('/')];
        var itemId = page.Rows.Last().Key[(collection.Length + 1)..];
        var edited = session.ApplyEdits([new(role + "/" + itemId + "/level", JsonSerializer.SerializeToElement(13))], null, Cancellation);
        Assert.True(edited.Ok, string.Join("; ", edited.Errors));
        Assert.True(session.Undo().Ok);
        ActionEffectData.AssertBytes(after, observer.Current);
        Assert.True(session.Undo().Ok);
        ActionEffectData.AssertBytes(before, observer.Current);
    }

    private static void AssertOrderedEffects(ActionPlan plan, string action)
    {
        if (action.EndsWith("items/import", StringComparison.Ordinal))
        {
            Assert.Equal(2, plan.Changes.Count);
            Assert.Null(plan.Changes[0].Effect);
            Assert.EndsWith("/import/serial", plan.Changes[1].ValueId, StringComparison.Ordinal);
            Assert.False(plan.Changes[1].OldExists);
            Assert.NotNull(plan.Changes[1].Effect);
            return;
        }
        var source = action == "transfer/deposit" ? "character/inventory_items/item-0"
            : action == "transfer/withdraw" ? "profile/bank_inventory_list/item-0" : "profile/lost_loot_inventory_list/item-2";
        var target = action == "transfer/deposit" ? "profile/bank_inventory_list/character:item-0"
            : "character/inventory_items/profile:" + (action == "transfer/withdraw" ? "item-0" : "item-2");
        var sourceCount = source[..source.LastIndexOf('/')] + "/count";
        var targetCount = target[..target.LastIndexOf('/')] + "/count";
        var expected = new List<string> { source, target, sourceCount, targetCount };
        if (action == "transfer/deposit") expected.AddRange(["character/slots/0/index", "character/slots/1/index"]);
        Assert.Equal(expected, plan.Changes.Select(change => change.ValueId));
        Assert.False(plan.Changes[0].NewExists);
        Assert.False(plan.Changes[1].OldExists);
        Assert.All(plan.Changes.Take(2), change =>
        {
            Assert.True(change.Effect!.Spec.ReadOnly);
            Assert.Contains("tab:Inventory", change.Effect.Path!);
            Assert.StartsWith("section:Gearbox character + profile", change.Effect.Path![0], StringComparison.Ordinal);
        });
        Assert.All(plan.Changes.Skip(2), change => Assert.Null(change.Effect));
    }

    private static void AssertProjection(ActionPlan plan, SessionEditResult result) =>
        Assert.Equal(plan.Changes.Select(change => new ValueChange(change.ValueId, change.OldValue, change.NewValue)), result.Changes);

}
