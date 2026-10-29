using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithCoverageSampleTests
{
    private readonly FinalFantasy16Plugin _plugin = new();

    [Theory]
    [InlineData("PPSA10665", "set-gil", 1)]
    [InlineData("PPSA10665", "set-ap", 3)]
    [InlineData("PPSA10665", "set-renown", 2)]
    [InlineData("PPSA13803", "set-gil", 1)]
    [InlineData("PPSA13803", "set-ap", 3)]
    public void SampleResourcePreviewPersistsWithExactCrcAndHistory(string title, string action, int id)
    {
        var file = FaithWriteTests.SampleFile(title);
        var original = file.ReadAllBytes();
        var inventory = FaithInventory.Parse(original, FaithContainer.Parse(original));
        var item = Assert.Single(inventory.Items, item => item.Id == id);
        var references = new ReferencePackStore([_plugin]).Pin(_plugin, title);
        using var document = _plugin.Open(file, references);
        using var session = new DocumentSession("sample-resource", file, _plugin, document, references: references);
        _ = session.BuildView();
        Assert.False(session.InvokeAction(action, new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement(1) }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var preview = session.PlanAction(action, new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement(1) }, scope: null, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        FaithWriteTests.AssertOnlyRanges(original, output, [(item.Offset + sizeof(int), sizeof(int))]);
        using var reopened = _plugin.Open(FaithSamples.WithBytes(file, output), references);
        _ = reopened.BuildView();
        Assert.Equal(1, reopened.GetValue(item.QuantityId).AsInt32());
        Assert.Equal(preview.Plan.Changes.Single(change => change.Derived).NewValue, reopened.GetValue("integrity/crc32"));
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(output, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void RetailRestoreUsesImportedStacksAndPreservesEveryOtherByte()
    {
        const string Action = "restore-imported-main-stacks";
        var file = FaithWriteTests.SampleFile("PPSA10665");
        var original = file.ReadAllBytes();
        var inventory = FaithInventory.Parse(original, FaithContainer.Parse(original));
        var selected = inventory.Items.Where(item => item.Id is >= 101 and <= 109 &&
            BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(item.Offset + sizeof(int))) > 1).Take(2).ToArray();
        Assert.Equal(2, selected.Length);
        using var document = _plugin.Open(file);
        using var session = new DocumentSession("sample-restore", file, _plugin, document);
        _ = session.BuildView();
        var planner = (IActionPlanningDocument)document;
        // Storage bindings exist even when rows have not been rendered or queried.
        Assert.True(document.ApplyEdits(selected.Select(item => new ValueEdit(item.QuantityId, FieldValue.Of(1))).ToArray()).Succeeded);
        var reduced = Assert.Single(document.Serialize()).Content.ToArray();
        var scope = new ActionScope(selected.Select(item => $"inventory/{item.Slot}").ToArray());
        var plan = planner.PlanAction(Action, new Dictionary<string, FieldValue>(), scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(2, plan.Changes.Count(change => !change.Derived));
        Assert.Equal(reduced, Assert.Single(document.Serialize()).Content.ToArray());
        var preview = session.PlanAction(Action, new Dictionary<string, JsonElement>(), scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void FilteredRestoreReachesUnloadedRowsAndHonorsExclusions()
    {
        const string Action = "restore-imported-main-stacks";
        const int FirstSlot = 1100;
        const int SecondSlot = 1101;
        const int RecordSize = 16;
        const int PayloadOffset = FaithContainer.HeaderSize + FaithBlock.HeaderSize;
        var original = FaithPerformanceData.CapacitySave();
        BinaryPrimitives.WriteInt32LittleEndian(original.AsSpan(PayloadOffset + FirstSlot * RecordSize), 101);
        BinaryPrimitives.WriteInt32LittleEndian(original.AsSpan(PayloadOffset + SecondSlot * RecordSize), 102);
        FaithTestData.FixCrc(original);
        using var fixture = new FaithSessionFile(original);
        using var document = _plugin.Open(fixture.File);
        using var session = new DocumentSession("filtered-restore", fixture.File, _plugin, document);
        _ = session.BuildView();
        Assert.True(document.ApplyEdits([new($"inventory/{FirstSlot}/quantity", FieldValue.Of(1)),
            new($"inventory/{SecondSlot}/quantity", FieldValue.Of(1))]).Succeeded);
        var reduced = Assert.Single(document.Serialize()).Content.ToArray();
        var query = new ActionQueryScope(FaithInventoryView.CollectionId,
            new() { Filters = new Dictionary<string, string> { ["category"] = "material" } }, session.State.Revision)
            { ExcludedRowKeys = [$"inventory/{SecondSlot}"] };
        var preview = session.PlanAction(Action, new Dictionary<string, JsonElement>(), new() { QueryScope = query }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal($"inventory/{FirstSlot}", Assert.Single(preview.Scope.RowKeys!));
        Assert.Equal($"inventory/{FirstSlot}/quantity", Assert.Single(preview.Plan.Changes, change => !change.Derived).ValueId);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(FirstSlot + 2, document.GetValue($"inventory/{FirstSlot}/quantity").AsInt32());
        Assert.Equal(1, document.GetValue($"inventory/{SecondSlot}/quantity").AsInt32());
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, Assert.Single(document.Serialize()).Content.ToArray());
        query = query with { ExcludedRowKeys = [$"inventory/{FirstSlot}", $"inventory/{SecondSlot}"], Revision = session.State.Revision };
        var empty = session.PlanAction(Action, new Dictionary<string, JsonElement>(), new() { QueryScope = query }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(empty.Plan.Changes);
        Assert.Empty(empty.Scope.RowKeys!);
        Assert.True(session.ApplyActionPlan(empty.PlanId!, empty.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(reduced, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
