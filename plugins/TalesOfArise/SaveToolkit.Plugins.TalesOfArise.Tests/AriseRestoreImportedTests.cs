using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseRestoreImportedTests
{
    private const string ActionId = "inventory/restore-imported";
    private const string FirstRow = "inventory/0/quantity";
    private const string SecondRow = "inventory/1/quantity";
    private static readonly IReadOnlyDictionary<string, FieldValue> NoParameters = new Dictionary<string, FieldValue>();

    [Fact]
    public void Restore_UsesOpeningQuantitiesBeforeFirstPreview_AndPreservesRaisedAndUnsupportedRows()
    {
        using var document = Open(new AriseTestContainer().Encrypt());
        _ = document.LoadInventory();
        Assert.True(document.ApplyEdits([new(FirstRow, FieldValue.Of(2L)), new(SecondRow, FieldValue.Of(0L))]).Succeeded);
        var before = Bytes(document);
        var scope = new ActionScope([SecondRow, FirstRow, "inventory/2/quantity", "inventory/3/quantity", "inventory/4/quantity"]);
        var plan = document.PlanAction(ActionId, NoParameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(new PlannedChange(SecondRow, "Peach Gel · quantity (raw ID 0x0002)", FieldValue.Of(0L), FieldValue.Of(15L)), Assert.Single(plan.Changes));
        Assert.Equal(scope.RowKeys!.Skip(1), plan.SkippedRows.Select(row => row.RowKey));
        Assert.All(plan.SkippedRows, row => Assert.NotEmpty(row.Reason));
        Assert.Contains(plan.Warnings, warning => warning.StartsWith(AriseDocument.ConsoleWarning, StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, warning => warning.Contains("opening", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, Bytes(document)); // Dismissing the preview performs no writes.

        Assert.True(document.ApplyActionPlan(ActionId, NoParameters, scope, plan).Succeeded);
        Assert.Equal(2, document.GetValue(FirstRow).AsInt64());
        Assert.Equal(15, document.GetValue(SecondRow).AsInt64());
        Assert.Equal(16, document.GetValue("inventory/2/quantity").AsInt64());
        Assert.Equal(5, document.GetValue("inventory/3/quantity").AsInt64());
        Assert.Equal(7, document.ReadInventoryRows().Single(row => row.Index == 4).Quantity);
        Assert.Empty(document.PlanAction(ActionId, NoParameters, new()).Changes);
        Assert.True(document.ApplyEdits([new(SecondRow, FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(FieldValue.Of(15L), Assert.Single(document.PlanAction(ActionId, NoParameters, new()).Changes).NewValue);
    }

    [Fact]
    public void EveryRecognisedId_RestoresOnlyItsQuantity_AndKeepsCipherSha1AndOtherBytesValid()
    {
        const int recognisedCount = 13;
        var fixture = new AriseTestContainer(reversePhysicalOrder: true);
        var inventory = fixture.Body("SaveData_ItemManager");
        for (var index = 0; index < recognisedCount; index++)
        {
            var offset = inventory + index * AriseTestContainer.ItemRecordSize;
            fixture.Payload[offset] = (byte)(index + 1);
            BinaryPrimitives.WriteUInt16LittleEndian(fixture.Payload.AsSpan(offset + 2), (ushort)(index + 1));
        }
        var original = fixture.Encrypt();
        using var document = Open(original);
        _ = document.LoadInventory();
        Assert.Equal(original, Bytes(document));
        Assert.True(document.ApplyEdits(Enumerable.Range(0, recognisedCount)
            .Select(index => new ValueEdit($"inventory/{index}/quantity", FieldValue.Of(0L))).ToArray()).Succeeded);
        var plan = document.PlanAction(ActionId, NoParameters, new());
        Assert.Empty(plan.Errors);
        Assert.Equal(recognisedCount, plan.Changes.Count);
        Assert.True(document.ApplyActionPlan(ActionId, NoParameters, new(), plan).Succeeded);
        var output = Bytes(document);
        Assert.Equal(original, output);
        Assert.Equal(fixture.Payload.AsSpan(0x20).ToArray(), AriseFormat.Decode(output).Payload.AsSpan(0x20).ToArray());
        Assert.Equal(AriseSampleTests.Hash(output.AsSpan(AriseFormat.CipherOffset)), output.AsSpan(AriseFormat.HashOffset, 20).ToArray());
        using var reopened = Open(output);
        _ = reopened.LoadInventory();
        Assert.Empty(reopened.PlanAction(ActionId, NoParameters, new()).Changes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(15, 0)]
    [InlineData(15, 15)]
    [InlineData(16, 0)]
    [InlineData(15, 16)]
    public void ImportedAndCurrentBounds_AreBothRequired(byte imported, byte current)
    {
        var fixture = new AriseTestContainer();
        var inventory = fixture.Body("SaveData_ItemManager");
        fixture.Payload[inventory] = imported;
        using var document = new AriseDocument("generated", AriseTestContainer.FileName,
            new([], fixture.Payload, fixture.Sections), SaveToolkit.Sdk.References.IReferenceContext.Empty);
        fixture.Payload[inventory] = current;
        _ = document.ExpandNode("inventory");
        var plan = document.PlanAction(ActionId, NoParameters, new([FirstRow]));
        Assert.Empty(plan.Errors);
        if (imported <= AriseDocument.ConsumableStackLimit && current < imported)
            Assert.Equal(FieldValue.Of((long)imported), Assert.Single(plan.Changes).NewValue);
        else
        {
            Assert.Empty(plan.Changes);
            Assert.Single(plan.SkippedRows);
        }
    }

    [Fact]
    public void Restore_RejectsInvalidScopesParametersAndTamperedOrStalePlans()
    {
        using var document = Open(new AriseTestContainer().Encrypt());
        Assert.NotEmpty(document.PlanAction(ActionId, NoParameters, new()).Errors);
        _ = document.LoadInventory();
        Assert.True(document.ApplyEdits([new(SecondRow, FieldValue.Of(0L))]).Succeeded);
        var scope = new ActionScope([SecondRow]);
        var plan = document.PlanAction(ActionId, NoParameters, scope);
        Assert.Empty(plan.Errors);
        var before = Bytes(document);
        Assert.False(document.ApplyActionPlan(ActionId, NoParameters, scope, plan with { Changes = [] }).Succeeded);
        Assert.False(document.ApplyActionPlan(ActionId, NoParameters, scope, plan with { Warnings = [] }).Succeeded);
        Assert.Equal(before, Bytes(document));
        Assert.NotEmpty(document.PlanAction(ActionId, new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(15L) }, scope).Errors);
        Assert.NotEmpty(document.PlanAction(ActionId, NoParameters, new([SecondRow, SecondRow])).Errors);
        Assert.NotEmpty(document.PlanAction(ActionId, NoParameters, new(["inventory/999/quantity"])).Errors);
        Assert.NotEmpty(document.PlanAction(ActionId, NoParameters, new() { QueryScope = new("inventory", new(), 0) }).Errors);
        Assert.NotEmpty(document.PlanAction(ActionId, NoParameters, scope with { QueryScope = new("wrong", new(), 0) }).Errors);
        Assert.Throws<InvalidOperationException>(() => document.InvokeAction(ActionId, NoParameters));
        Assert.True(document.ApplyEdits([new(SecondRow, FieldValue.Of(1L))]).Succeeded);
        Assert.False(document.ApplyActionPlan(ActionId, NoParameters, scope, plan).Succeeded);
        Assert.Equal(1, document.GetValue(SecondRow).AsInt64());
    }

    [Fact]
    public void EmptyExplicitSelection_IsANoOp()
    {
        using var document = Open(new AriseTestContainer().Encrypt());
        _ = document.ExpandNode("inventory");
        var before = Bytes(document);
        var scope = new ActionScope([]);
        var plan = document.PlanAction(ActionId, NoParameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Empty(plan.SkippedRows);
        Assert.True(document.ApplyActionPlan(ActionId, NoParameters, scope, plan).Succeeded);
        Assert.Equal(before, Bytes(document));
    }

    [Fact]
    public void MultiRowRestore_IsOneCoreUndoGroup_WithEachRowsOwnImportedTarget()
    {
        var original = new AriseTestContainer().Encrypt();
        var storage = Path.Combine(TestScratch.ProcessFolder, $"restore-{Guid.NewGuid():N}");
        var set = SaveSetImporter.Import([new ImportSource($"{AriseTestContainer.TitleId}/sdimg_generated/{AriseTestContainer.FileName}",
            () => new MemoryStream(original))], storage);
        var file = Assert.Single(set.AllDiskFiles);
        var plugin = new TalesOfArisePlugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("arise-multi-restore", file, plugin, document);
        _ = session.BuildView();
        _ = session.Expand("inventory");
        _ = session.QueryCollection("inventory", new());
        var zero = JsonSerializer.SerializeToElement("0");
        Assert.True(session.ApplyEdits([new(FirstRow, zero), new(SecondRow, zero)]).Ok);
        var reduced = Bytes(document);
        var preview = session.PlanAction(ActionId, new Dictionary<string, JsonElement>(), new ActionScope([SecondRow, FirstRow]));
        Assert.NotNull(preview.PlanId);
        Assert.Equal(new[] { FieldValue.Of(15L), FieldValue.Of(1L) }, preview.Plan.Changes.Select(change => change.NewValue));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(original, Bytes(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, Bytes(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(original, Bytes(document));
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, Bytes(document));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FilledInventory_RestorePlanningAndUnloadedApplyStayWithinExistingAllocationBounds(bool allRows)
    {
        const int slotCount = 934;
        const int kibibyte = 1024;
        var fixture = new AriseTestContainer();
        var inventory = fixture.Body("SaveData_ItemManager");
        for (var index = 0; index < slotCount; index++)
        {
            var offset = inventory + index * AriseTestContainer.ItemRecordSize;
            fixture.Payload[offset] = AriseDocument.ConsumableStackLimit;
            BinaryPrimitives.WriteUInt16LittleEndian(fixture.Payload.AsSpan(offset + 2), 1);
        }
        using var document = new AriseDocument("generated", AriseTestContainer.FileName,
            new([], fixture.Payload, fixture.Sections), SaveToolkit.Sdk.References.IReferenceContext.Empty);
        // Stage reductions in the owned fixture buffer without rendering or binding any rows.
        for (var index = 0; index < slotCount; index++) fixture.Payload[inventory + index * AriseTestContainer.ItemRecordSize] = 0;
        var scope = new ActionScope(allRows ? null : ["inventory/933/quantity"]);
        _ = document.ExpandNode("inventory");
        _ = document.PlanAction(ActionId, NoParameters, scope);
        var beforePlanning = GC.GetAllocatedBytesForCurrentThread();
        var plan = document.PlanAction(ActionId, NoParameters, scope);
        var planningBytes = GC.GetAllocatedBytesForCurrentThread() - beforePlanning;
        Assert.Empty(plan.Errors);
        Assert.Equal(allRows ? slotCount : 1, plan.Changes.Count);
        Assert.True(planningBytes < 1024 * kibibyte, $"Restore planning allocated {planningBytes:N0} B.");
        Assert.Throws<KeyNotFoundException>(() => document.GetValue("inventory/933/quantity"));
        var beforeApply = GC.GetAllocatedBytesForCurrentThread();
        var result = document.ApplyActionPlan(ActionId, NoParameters, scope, plan);
        var applyBytes = GC.GetAllocatedBytesForCurrentThread() - beforeApply;
        Assert.True(result.Succeeded);
        Assert.Equal(15, document.GetValue("inventory/933/quantity").AsInt64());
        if (!allRows) Assert.True(applyBytes < 512 * kibibyte, $"One-row restore apply allocated {applyBytes:N0} B.");
    }

    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
    private static AriseDocument Open(byte[] bytes) => Assert.IsType<AriseDocument>(new TalesOfArisePlugin().Open(
        MemorySaveSet.SingleFile(AriseTestContainer.FileName, bytes, AriseTestContainer.TitleId)));
}
