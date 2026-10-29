using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithQuantityBoundaryTests
{
    // Pin the public policy and fixture layout rather than deriving expectations from the planner.
    private const int MaximumTarget = 127811;
    private const int PayloadOffset = 32;
    private const int RecordSize = 16;
    private const int QuantityOffset = sizeof(int);
    private const int UpgradeQuantity = 567;
    private const string UpgradeAlias = "capacity/506/main/1";
    private readonly FinalFantasy16Plugin _plugin = new();

    public static TheoryData<int, int> Boundaries
    {
        get
        {
            var cases = new TheoryData<int, int>();
            foreach (var source in new[] { -1, 0, UpgradeQuantity, MaximumTarget, int.MaxValue })
                foreach (var target in new[] { 0, UpgradeQuantity, MaximumTarget })
                    cases.Add(source, target);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void SelectedBoundariesHaveExactBytesChangesSkipsAndOneUndoRedo(int source, int target)
    {
        var original = Save(source);
        var expected = original.ToArray();
        var expectedChanges = new List<(string ValueId, FieldValue OldValue, FieldValue NewValue)>();
        var expectedSkips = new List<(string RowKey, string Reason)>();
        for (var slot = 0; slot < 2; slot++)
        {
            var old = slot == 0 ? source : UpgradeQuantity;
            if (old < 0 || old == target)
            {
                expectedSkips.Add(($"inventory/{slot}", old < 0
                    ? "Negative stack has unknown semantics; unchanged."
                    : "Already at the requested quantity."));
                continue;
            }
            BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(PayloadOffset + slot * RecordSize + QuantityOffset), target);
            expectedChanges.Add(($"inventory/{slot}/quantity", FieldValue.Of(old), FieldValue.Of(target)));
            if (slot == 1) expectedChanges.Add((UpgradeAlias, FieldValue.Of(old), FieldValue.Of(target)));
        }
        FaithTestData.FixCrc(expected);
        var oldCrc = FieldValue.Of((ulong)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(4)));
        var newCrc = FieldValue.Of((ulong)BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(4)));
        if (oldCrc != newCrc) expectedChanges.Add(("integrity/crc32", oldCrc, newCrc));

        using var fixture = new FaithSessionFile(original);
        using var document = _plugin.Open(fixture.File);
        using var session = new DocumentSession("quantity-boundaries", fixture.File, _plugin, document);
        _ = session.BuildView();
        var state = session.State;
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue> { [FaithQuantityPlanning.QuantityParameter] = FieldValue.Of(target) };
        var scope = new ActionScope(["inventory/0", "inventory/1"]);
        var plan = planner.PlanAction(FaithQuantityPlanning.ActionId, parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(expectedChanges, plan.Changes.Select(change => (change.ValueId, change.OldValue, change.NewValue)));
        Assert.Equal(expectedSkips, plan.SkippedRows.Select(row => (row.RowKey, row.Reason)));
        Assert.All(plan.Changes, change => Assert.Equal(change.ValueId is UpgradeAlias or "integrity/crc32", change.Derived));
        Assert.All(plan.Changes, change => { Assert.True(change.OldExists); Assert.True(change.NewExists); });
        Assert.Equal(plan.Changes, planner.PlanAction(FaithQuantityPlanning.ActionId, parameters, scope).Changes);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(state, session.State);

        var incoming = new Dictionary<string, JsonElement>
            { [FaithQuantityPlanning.QuantityParameter] = JsonSerializer.SerializeToElement(target) };
        var preview = session.PlanAction(FaithQuantityPlanning.ActionId, incoming, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(plan.Changes, preview.Plan.Changes);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        var result = session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(expectedChanges, result.Changes.Select(change => (change.ValueId, change.OldValue, change.NewValue)));
        Assert.Equal(plan.Warnings, result.Warnings);
        Assert.Contains(result.Warnings, warning => warning.Contains("Not validated on console", StringComparison.Ordinal));
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(expected, output);
        using var reopened = _plugin.Open(FaithTestData.File(output));
        _ = reopened.BuildView();
        Assert.Equal(source < 0 ? source : target, reopened.GetValue("inventory/0/quantity").AsInt32());
        Assert.Equal(target, reopened.GetValue("inventory/1/quantity").AsInt32());
        Assert.Equal(target, reopened.GetValue(UpgradeAlias).AsInt32());

        if (expectedChanges.Count == 0)
        {
            Assert.Equal(state, session.State);
            Assert.False(session.State.CanUndo);
            Assert.False(session.State.CanRedo);
        }
        else
        {
            Assert.True(session.State.CanUndo);
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.False(session.State.IsDirty);
            Assert.False(session.State.CanUndo);
            Assert.True(session.Redo().Ok);
            Assert.Equal(expected, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.False(session.State.CanRedo);
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        }
        Assert.Equal(original, fixture.File.ReadAllBytes());
    }

    private static byte[] Save(int quantity)
    {
        var payload = FaithTestData.Items();
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(QuantityOffset), quantity);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(RecordSize), 506);
        // Distinct flags/padding, an unknown block and a trailer must survive apply and history.
        payload[2 * sizeof(int)] = 0x81;
        payload[3 * sizeof(int)] = 0xA5;
        payload[RecordSize + 2 * sizeof(int)] = 0x42;
        payload[RecordSize + 3 * sizeof(int)] = 0xFF;
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload),
            (99, 1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        bytes = [.. bytes, 0x12, 0x34];
        FaithTestData.FixCrc(bytes);
        return bytes;
    }
}
