using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ActionEffectSampleTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void EverySampleStructuralActionHasExactEffectsAndByteCompleteHistory(string title)
    {
        var plugin = new GearboxPlugin();
        var count = 0;
        var appliedCount = 0;
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(file => plugin.Probe(file).IsMatch))
        {
            using var document = (GearboxDocument)plugin.Open(file);
            using var session = new DocumentSession("sample-effects", file, plugin, document);
            _ = session.BuildView();
            var before = ActionEffectData.Bytes(document);
            Assert.Equal(file.ReadAllBytes(), before[file.RelativePath]);
            var entry = document.Inventory.Entries.First(item => document.Inventory.Info(item.Id).Complete);
            var serial = entry.Serial.ToArray();
            foreach (var action in new[] { "items/import", "items/duplicate", "items/remove" })
            {
                _ = session.BuildView();
                _ = session.QueryCollection(entry.List, new() { IncludeEmpty = true, Limit = 200 }, null, Cancellation);
                _ = session.Expand("detail/" + entry.Id);
                var parameters = action == "items/import" ? Code(serial) : new Dictionary<string, JsonElement>();
                var scope = action == "items/import" ? new ActionScope() : new([entry.Id]);
                if (Exercise(session, document, file.Directory.SaveSet, plugin, action, parameters, scope,
                    requireEffects: action == "items/import")) appliedCount++;
                ActionEffectData.AssertBytes(before, document);
                count++;
            }
        }
        Assert.True(appliedCount > 0);
        TestContext.Current.TestOutputHelper!.WriteLine($"{title}: {count} standalone attempts; {appliedCount} applied; {count - appliedCount} capacity rejections");
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void EverySamplePairTransfersThroughProductionHostAndExport(string title)
    {
        var plugin = new GearboxPlugin();
        var set = SampleSaves.Load(title);
        var count = 0;
        var appliedCount = 0;
        foreach (var file in set.AllDiskFiles.Where(file => file.FileName == "1"))
        foreach (var action in new[] { "transfer/deposit", "transfer/withdraw", "transfer/claim" })
        {
            using var workspace = new Workspace("sample-effects", "Sample effects", set,
                new PluginCatalog([plugin]), new ReferencePackStore([plugin]));
            var proposal = Assert.Single(plugin.ProposeGroups(file));
            using var source = (GearboxGroupDocument)plugin.OpenGroup(GearboxGroupTests.Input(set, proposal),
                SaveToolkit.Sdk.References.IReferenceContext.Empty);
            var list = action == "transfer/deposit" ? GearboxInventory.Backpack
                : action == "transfer/withdraw" ? GearboxInventory.Bank : GearboxInventory.LostLoot;
            var member = action == "transfer/deposit" ? source.Character : source.Profile;
            var entry = member.Inventory.Entries.FirstOrDefault(item => item.List == list && member.Inventory.Info(item.Id).Complete);
            if (entry is null)
            {
                TestContext.Current.TestOutputHelper!.WriteLine($"{action}: not evaluated for generation {file.Directory.Generation}, backup={file.Directory.IsBackup}; no complete source serial");
                continue;
            }
            var role = action == "transfer/deposit" ? "character" : "profile";
            var opened = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(file.Id, "gearbox")).Token);
            var session = opened.Session;
            _ = session.BuildView();
            // The host owns the live group candidate; snapshots below go through session serialization.
            var before = session.Serialize(Cancellation);
            if (Exercise(session, null, set, plugin, action, [], new([role + "/" + list + "/" + entry.Id]),
                requireEffects: true, workspace)) appliedCount++;
            Assert.False(session.State.IsDirty);
            Assert.Equal(before.Select(output => output.RelativePath), session.Serialize(Cancellation).Select(output => output.RelativePath));
            count++;
        }
        Assert.True(appliedCount > 0);
        TestContext.Current.TestOutputHelper!.WriteLine($"{title}: {count} grouped attempts; {appliedCount} applied; {count - appliedCount} capacity rejections");
    }

    private static bool Exercise(DocumentSession session, ISaveDocument? document, ISaveSet set, GearboxPlugin plugin,
        string action, Dictionary<string, JsonElement> parameters, ActionScope scope, bool requireEffects,
        Workspace? workspace = null)
    {
        var originals = document is null
            ? plugin.ProposeGroups(set.AllFiles.First(file => file.Id == session.File.Id)).Single().Members
                .ToDictionary(member => member.RelativePath, member => set.FindFile(member.RelativePath)!.ReadAllBytes())
            : ActionEffectData.Bytes(document);
        Dictionary<string, byte[]> Capture()
        {
            if (document is not null) return ActionEffectData.Bytes(document);
            var result = new Dictionary<string, byte[]>(originals);
            foreach (var output in session.Serialize(Cancellation)) result[output.RelativePath] = output.Content.ToArray();
            return result;
        }
        var before = Capture();
        var state = session.State;
        var watch = Stopwatch.StartNew();
        var allocation = GC.GetAllocatedBytesForCurrentThread();
        var preview = session.PlanAction(action, parameters, scope, null, Cancellation);
        if (preview.Plan.Errors.Count > 0)
        {
            // Imported samples can already be full. Keep this a rejection check, never an
            // apply success, and accept only the documented destination-capacity policy.
            Assert.All(preview.Plan.Errors, error => Assert.True(
                error.Message.StartsWith("Destination capacity policy (", StringComparison.Ordinal) ||
                error.Message.StartsWith("No SDU record; destination growth is limited to the base-capacity policy (", StringComparison.Ordinal),
                "Unexpected rejection: " + error.Message));
            SelectionByteAssertions.Equal(before, Capture());
            Assert.Equal(state, session.State);
            TestContext.Current.TestOutputHelper!.WriteLine($"{action}: capacity rejection verified; apply not evaluated");
            return false;
        }
        Assert.Empty(preview.Plan.Errors);
        Assert.NotEmpty(preview.Plan.Changes);
        if (requireEffects) Assert.Contains(preview.Plan.Changes, change => change.Effect is not null);
        SelectionByteAssertions.Equal(before, Capture());
        Assert.Equal(state, session.State);
        var applied = session.ApplyActionPlan(preview.PlanId!, preview.Revision, Cancellation);
        Assert.True(applied.Ok, string.Join("; ", applied.Errors));
        Assert.Equal(preview.Plan.Changes.Select(change => new ValueChange(change.ValueId, change.OldValue, change.NewValue)), applied.Changes);
        var after = Capture();
        Assert.Contains(after.Keys, key => !before[key].AsSpan().SequenceEqual(after[key]));
        foreach (var output in after)
        {
            var original = set.FindFile(output.Key)!;
            using var reopened = plugin.Open(GearboxTestData.Overlay(original, output.Value));
            Assert.Equal(output.Value, reopened.Serialize().Single().Content.ToArray());
        }
        if (workspace is not null)
        {
            var export = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.Everything }, Cancellation);
            Assert.Equal(after.Count, export.Overrides.Count);
            foreach (var output in after) Assert.Equal(output.Value, export.Overrides[output.Key].ToArray());
        }
        Assert.True(session.Undo().Ok);
        SelectionByteAssertions.Equal(before, Capture());
        Assert.True(session.Redo().Ok);
        SelectionByteAssertions.Equal(after, Capture());
        Assert.True(session.Undo().Ok);
        SelectionByteAssertions.Equal(before, Capture());
        Assert.False(session.State.CanUndo);
        TestContext.Current.TestOutputHelper!.WriteLine($"{action}: {watch.Elapsed.TotalMilliseconds:F1} ms, {GC.GetAllocatedBytesForCurrentThread() - allocation} B (preview/apply/export/reopen/history)");
        return true;
    }

    private static Dictionary<string, JsonElement> Code(byte[] serial) => new()
    { ["code"] = JsonSerializer.SerializeToElement(Convert.ToBase64String(serial)) };
}
