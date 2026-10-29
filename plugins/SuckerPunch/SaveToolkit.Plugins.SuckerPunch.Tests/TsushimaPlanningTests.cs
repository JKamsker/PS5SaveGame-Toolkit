using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;
using static SaveToolkit.Plugins.SuckerPunch.Tests.QuantityPlanningTestData;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaPlanningTests
{
    private const string ActionId = "inventory/set-goods-quantity";

    [Fact]
    public void EverySamplePlansPurelyAndChangesOnlySupportedCountWordsAndChecksum()
    {
        var plugin = new SuckerPunchPlugin();
        var files = SampleSaves.Load(TsushimaTitle).AllDiskFiles.Where(file => plugin.Probe(file).IsMatch).ToArray();
        Assert.Equal(25, files.Length);
        foreach (var file in files)
        {
            using var document = Open(file);
            var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
            var original = file.ReadAllBytes();
            var parameters = Target(7);
            var scope = new ActionScope(["Supplies", "Flowers", "Hide", "TestItem", "ArrowNormal"]);
            var plan = planner.PlanAction(ActionId, parameters, scope);
            Assert.Empty(plan.Errors);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.Contains(plan.SkippedRows, row => row.RowKey == "TestItem");
            Assert.Contains(plan.SkippedRows, row => row.RowKey == "ArrowNormal");
            Assert.Contains("not a capacity refill", Assert.Single(plan.Warnings));

            var result = planner.ApplyActionPlan(ActionId, parameters, scope, plan);
            Assert.True(result.Succeeded);
            Assert.Equal(plan.Changes.Select(change => (change.ValueId, change.OldValue, change.NewValue)),
                result.Changes.Select(change => (change.ValueId, change.OldValue, change.NewValue)));
            var expected = original.ToArray();
            var anchor = expected.AsSpan().IndexOf("\x11\0\0\0LegendsInvestment"u8);
            foreach (var change in plan.Changes.Where(change => !change.Derived))
            {
                var name = change.ValueId.Split('/')[1];
                Assert.True(name is "Supplies" or "Flowers" or "Hide");
                var key = new byte[sizeof(uint) + name.Length];
                BinaryPrimitives.WriteUInt32LittleEndian(key, (uint)name.Length);
                Encoding.ASCII.GetBytes(name, key.AsSpan(sizeof(uint)));
                var countOffset = anchor + expected.AsSpan(anchor).IndexOf(key) + key.Length + sizeof(uint);
                BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(countOffset), 7);
            }

            RepairChecksum(expected);
            var edited = Assert.Single(document.Serialize()).Content.ToArray();
            Assert.Equal(expected, edited);
            using var reopened = Open(WithBytes(edited));
            foreach (var change in plan.Changes)
            {
                Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId));
            }

            Assert.True(document.ApplyEdits(plan.Changes.Where(change => !change.Derived)
                .Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void CoreRequiresPreviewAndRecordsOneUndoRedoGroupIncludingChecksum()
    {
        var plugin = new SuckerPunchPlugin();
        var file = Assert.IsType<DiskSaveFile>(Slot("suckerpunchUser1"));
        var document = plugin.Open(file);
        using var session = new DocumentSession("goods", file, plugin, document);
        _ = session.BuildView();
        var parameters = new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("7") };
        Assert.False(session.InvokeAction(ActionId, parameters, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var preview = session.PlanAction(ActionId, parameters, new ActionScope(["Supplies", "Flowers"]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.False(session.State.CanUndo);
        Assert.Equal(3, preview.Plan.Changes.Count);
        Assert.True(preview.Plan.Changes.Single(change => change.ValueId == "container/word2").Derived);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.NotEqual(file.ReadAllBytes(), edited);
        Assert.Equal(3, session.GetChanges(cancellationToken: TestContext.Current.CancellationToken).Changes.Count);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Empty(session.GetChanges(cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
        var stale = session.PlanAction(ActionId, parameters, scope: null, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(session.ApplyEdits([new IncomingEdit(SuppliesId, JsonSerializer.SerializeToElement("8"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var beforeRejectedApply = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.True(session.ApplyActionPlan(stale.PlanId!, stale.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
        Assert.Equal(beforeRejectedApply, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32768)]
    public void InvalidTargetsRejectAtomically(long value)
    {
        using var document = Open(Slot("suckerpunchUser1"));
        var original = Assert.Single(document.Serialize()).Content.ToArray();
        var planner = (IActionPlanningDocument)document;
        var parameters = Target(value);
        var plan = planner.PlanAction(ActionId, parameters, new ActionScope());
        Assert.NotEmpty(plan.Errors);
        Assert.False(planner.ApplyActionPlan(ActionId, parameters, new ActionScope(), plan).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void MissingWrongTypeAndExtraParametersCannotBypassValidation()
    {
        using var document = Open(Slot("suckerpunchUser1"));
        var planner = (IActionPlanningDocument)document;
        Dictionary<string, FieldValue>[] invalid = [[], new() { ["quantity"] = FieldValue.Of("7") },
            new() { ["quantity"] = FieldValue.Of(Int128.MaxValue) },
            new() { ["quantity"] = FieldValue.Of(7L), ["unlock"] = FieldValue.Of(true) }];
        Assert.All(invalid, parameters => Assert.NotEmpty(planner.PlanAction(ActionId, parameters, new ActionScope()).Errors));
        Assert.Throws<KeyNotFoundException>(() => planner.PlanAction("unknown", Target(7), new ActionScope()));
    }

    [Fact]
    public void LockedOversizedAndExcludedRecordsAreSkipped()
    {
        var bytes = Slot("suckerpunchUser1").ReadAllBytes();
        var anchor = bytes.AsSpan().IndexOf("\x11\0\0\0LegendsInvestment"u8);
        var suppliesData = anchor + bytes.AsSpan(anchor).IndexOf("Supplies"u8) + "Supplies".Length;
        var flowersCount = anchor + bytes.AsSpan(anchor).IndexOf("Flowers"u8) + "Flowers".Length + sizeof(uint);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(suppliesData), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(flowersCount), 32768);
        RepairChecksum(bytes);
        using var document = Open(WithBytes(bytes));
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope(["Supplies", "Flowers", "ArrowNormal", "IngredCoreOre"]);
        var plan = planner.PlanAction(ActionId, Target(7), scope);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Equal(4, plan.SkippedRows.Count);
        Assert.True(planner.ApplyActionPlan(ActionId, Target(7), scope, plan).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EmptySelectionIsNotDocumentScopeAndUnknownOrQueryScopesReject()
    {
        using var document = Open(Slot("suckerpunchUser1"));
        var planner = (IActionPlanningDocument)document;
        Assert.Empty(planner.PlanAction(ActionId, Target(7), new ActionScope([])).Changes);
        Assert.NotEmpty(planner.PlanAction(ActionId, Target(7), new ActionScope()).Changes);
        Assert.NotEmpty(planner.PlanAction(ActionId, Target(7), new ActionScope(["unknown"])).Errors);
        var query = new ActionScope { QueryScope = new ActionQueryScope("inventory", new QueryableCollectionQuery(), 0) };
        Assert.NotEmpty(planner.PlanAction(ActionId, Target(7), query).Errors);
    }

    [Fact]
    public void ChangedOrForgedPlanCannotBeAppliedEvenWhenCalledDirectly()
    {
        using var document = Open(Slot("suckerpunchUser1"));
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope();
        var plan = planner.PlanAction(ActionId, Target(7), scope);
        Assert.False(planner.ApplyActionPlan(ActionId, Target(7), scope, plan with { Changes = [] }).Succeeded);
        Assert.True(document.ApplyEdits([Edit(SuppliesId, 86)]).Succeeded);
        var before = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.False(planner.ApplyActionPlan(ActionId, Target(7), scope, plan).Succeeded);
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void UnobservedContainerVersionsCannotUseTheNewBulkAction()
    {
        var bytes = Slot("suckerpunchUser1").ReadAllBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 50);
        RepairChecksum(bytes);
        using var document = Open(WithBytes(bytes));
        var planner = (IActionPlanningDocument)document;
        var plan = planner.PlanAction(ActionId, Target(7), new ActionScope());
        Assert.NotEmpty(plan.Errors);
        Assert.False(planner.ApplyActionPlan(ActionId, Target(7), new ActionScope(), new ActionPlan()).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EveryYoteiSampleRejectsTsushimaGoodsPlansAndFieldIds()
    {
        var plugin = new SuckerPunchPlugin();
        var files = SampleSaves.Load("PPSA26344").AllDiskFiles.Where(file => plugin.Probe(file).IsMatch).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var document = plugin.Open(file);
            _ = document.BuildView();
            var planner = (IActionPlanningDocument)document;
            var plan = planner.PlanAction(ActionId, Target(7), new ActionScope());
            Assert.NotEmpty(plan.Errors);
            Assert.False(planner.ApplyActionPlan(ActionId, Target(7), new ActionScope(), new ActionPlan()).Succeeded);
            Assert.False(document.ApplyEdits([Edit(SuppliesId, 7)]).Succeeded);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }
}
