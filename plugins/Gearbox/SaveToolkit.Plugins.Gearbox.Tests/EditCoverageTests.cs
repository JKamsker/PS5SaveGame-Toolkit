using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class EditCoverageTests
{
    private static readonly Dictionary<string, FieldValue> Empty = [];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchLevel_PreviewsSelectedKnownSerialsAndPreservesOtherBits(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands);
        var before = document.Serialize().Single().Content.ToArray();
        var scope = new ActionScope(["item-2", "item-0"]);
        var plan = document.PlanAction("items/match-level", Empty, scope);
        Assert.Empty(plan.Errors);
        var change = Assert.Single(plan.Changes);
        Assert.Equal("item-0/level", change.ValueId);
        Assert.Equal(FieldValue.Of(12), change.OldValue);
        Assert.Equal(FieldValue.Of(20), change.NewValue);
        Assert.Equal("item-2", Assert.Single(plan.SkippedRows).RowKey);
        Assert.Contains(plan.Warnings, w => w.StartsWith("Not validated on console", StringComparison.Ordinal));
        Assert.Equal(before, document.Serialize().Single().Content.ToArray()); // Cancel: discard preview.
        var serial = document.Inventory.Find("item-0").Serial.ToArray();
        var outcome = document.ApplyActionPlan("items/match-level", Empty, scope, plan);
        Assert.True(outcome.Succeeded);
        var updated = document.Inventory.Find("item-0").Serial;
        Assert.Equal(SerialWrites.Set(serial, wonderlands, "level", 20), updated);
        var oldPayload = IndependentSerialProbe.Decode(serial);
        var newPayload = IndependentSerialProbe.Decode(updated); // Independently checks repaired CRC.
        var offset = document.Inventory.Info("item-0").LevelOffset!.Value;
        Assert.Equal(serial[..5], updated[..5]);
        for (var bit = 0; bit < oldPayload.Length * 8; bit++)
            if (bit < offset || bit >= offset + 7)
                Assert.Equal((oldPayload[bit / 8] >> (bit % 8)) & 1, (newPayload[bit / 8] >> (bit % 8)) & 1);
        AssertHistory(document, outcome, before);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void Marks_PreviewRawFlagsSkipConflictsAndUnknownSerials(bool wonderlands, int mark)
    {
        using var document = EditCoverageData.Create(wonderlands);
        if (mark == 0) document.Inventory.Find("item-0").Field.Message!.SetInteger("flags", 0x103);
        var parameters = new Dictionary<string, FieldValue> { ["mark"] = FieldValue.Of(mark) };
        var scope = new ActionScope(["item-2", "item-1", "item-0"]);
        var before = document.Serialize().Single().Content.ToArray();
        var serials = document.Inventory.Entries.Select(e => e.Serial.ToArray()).ToArray();
        var plan = document.PlanAction("items/mark", parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(2, plan.SkippedRows.Count);
        var change = Assert.Single(plan.Changes);
        Assert.Equal("item-0/flags", change.ValueId);
        Assert.Equal(FieldValue.Of(mark == 0 ? 0x103 : 0x101), change.OldValue);
        Assert.Equal(FieldValue.Of(mark == 0 ? 0x101 : mark == 1 ? 0x103 : 0x105), change.NewValue);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        var outcome = document.ApplyActionPlan("items/mark", parameters, scope, plan);
        Assert.True(outcome.Succeeded);
        Assert.Equal(0x101, document.Inventory.Find("item-0").Field.Message!.Integer("flags") & ~6L);
        Assert.Equal(7, document.Inventory.Find("item-1").Field.Message!.Integer("flags"));
        Assert.Equal(1, document.Inventory.Find("item-2").Field.Message!.Integer("flags"));
        Assert.Equal(serials, document.Inventory.Entries.Select(e => e.Serial.ToArray()).ToArray());
        AssertHistory(document, outcome, before);
        Assert.Empty(document.PlanAction("items/mark", parameters, new(["item-0"])).Changes);
    }

    [Theory]
    [InlineData("items/match-level")]
    [InlineData("items/mark")]
    public void ItemActions_RejectImplicitForeignDuplicateAndWrongCollectionSelections(string action)
    {
        using var document = EditCoverageData.Create(false);
        var parameters = new Dictionary<string, FieldValue> { ["mark"] = FieldValue.Of(1) };
        Assert.NotEmpty(document.PlanAction(action, parameters, new()).Errors);
        Assert.Empty(document.PlanAction(action, parameters, new([])).Changes);
        Assert.NotEmpty(document.PlanAction(action, parameters, new(["foreign"])).Errors);
        Assert.NotEmpty(document.PlanAction(action, parameters, new(["item-0", "item-0"])).Errors);
        var scope = new ActionScope(["item-0"]) { QueryScope = new("catalogue", new(), 0) };
        Assert.NotEmpty(document.PlanAction(action, parameters, scope).Errors);
        using var profile = EditCoverageData.Create(false, profile: true);
        Assert.NotEmpty(profile.PlanAction("items/match-level", Empty, new([])).Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actions_RejectChangedOrForgedPlansAndInvalidParameters(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands);
        var scope = new ActionScope(["item-0"]);
        var plan = document.PlanAction("items/match-level", Empty, scope);
        var bytes = document.Serialize().Single().Content.ToArray();
        Assert.False(document.ApplyActionPlan("items/match-level", Empty, scope, plan with { Changes = [] }).Succeeded);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
        document.Root.SetInteger("experience_points", int.MaxValue);
        Assert.False(document.ApplyActionPlan("items/match-level", Empty, scope, plan).Succeeded);
        var clipped = document.PlanAction("items/match-level", Empty, scope);
        Assert.All(clipped.Changes, c => Assert.Equal(FieldValue.Of(wonderlands ? 40 : 72), c.NewValue));
        foreach (var mark in new[] { -1, 3 })
            Assert.NotEmpty(document.PlanAction("items/mark", new Dictionary<string, FieldValue> { ["mark"] = FieldValue.Of(mark) }, scope).Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchLevel_SkipsOutOfPolicyLevelsAndRejectsInvalidCharacterXp(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands, root =>
        {
            var serialField = root.Children("inventory_items").First().Last("item_serial_number")!;
            var serial = (byte[])serialField.Value!;
            var info = new SerialReader(wonderlands).Read(serial);
            serialField.Set(SerialEnvelope.Decode(serial, wonderlands).Patch(info.LevelOffset!.Value, 7, 127));
        });
        var before = document.Serialize().Single().Content.ToArray();
        var scope = new ActionScope(["item-0"]);
        var plan = document.PlanAction("items/match-level", Empty, scope);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Single(plan.SkippedRows);
        Assert.True(document.ApplyActionPlan("items/match-level", Empty, scope, plan).Succeeded);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        document.Root.SetInteger("experience_points", -1);
        Assert.NotEmpty(document.PlanAction("items/match-level", Empty, scope).Errors);
    }

    internal static void AssertHistory(GearboxDocument document, EditOutcome outcome, byte[] before)
    {
        Assert.NotNull(outcome.Revert);
        var after = document.Serialize().Single().Content.ToArray();
        Assert.NotEqual(before, after);
        var redo = document.RestoreMemento(outcome.Revert);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        _ = document.RestoreMemento(redo);
        Assert.Equal(after, document.Serialize().Single().Content.ToArray());
    }
}
