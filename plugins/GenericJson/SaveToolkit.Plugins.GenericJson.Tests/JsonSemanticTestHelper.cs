using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.GenericJson.Tests;

internal static class JsonSemanticTestHelper
{
    public static IReadOnlyList<ActionNode> Actions(JsonSessionFixture fixture) =>
        JsonTestViews.Walk(fixture.Session.BuildView(), id => fixture.Session.Expand(id))
            .OfType<ActionNode>().DistinctBy(action => action.ActionId).ToArray();

    public static ActionNode SetFor(JsonSessionFixture fixture, string rawLabel) => Actions(fixture).Single(a =>
        a.ActionId.StartsWith("semantic/", StringComparison.Ordinal) && a.ActionId.EndsWith("/set", StringComparison.Ordinal)
        && fixture.Fields().Single(f => f.ValueId == "value/" + a.ActionId.Split('/')[1]).Label == rawLabel);

    public static ActionPlan ApplyAndVerify(JsonSessionFixture fixture, string actionId, params (string Name, FieldValue Value)[] parameters)
    {
        Assert.Contains(Actions(fixture), action => action.ActionId == actionId && action.SupportsPlanning);
        var before = fixture.Bytes;
        var state = fixture.Session.State;
        var preview = fixture.Session.PlanAction(actionId, JsonArrayEditingTests.Parameters(parameters), expectedRevision: fixture.Session.State.Revision);
        Assert.True(preview.Plan.Errors.Count == 0, string.Join("; ", preview.Plan.Errors.Select(e => e.Message)));
        Assert.NotNull(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Changes);
        Assert.Contains(preview.Plan.Warnings, w => w.Contains("game may reject", StringComparison.Ordinal));
        Assert.True(before.AsSpan().SequenceEqual(fixture.Bytes));
        Assert.Equal(state, fixture.Session.State);
        var applied = fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision);
        Assert.True(applied.Ok, string.Join("; ", applied.Errors.Select(e => e.Message)));
        Assert.Equal(preview.Plan.Changes.Select(c => new ValueChange(c.ValueId, c.OldValue, c.NewValue)).OrderBy(c => c.ValueId, StringComparer.Ordinal),
            applied.Changes.OrderBy(c => c.ValueId, StringComparer.Ordinal));
        Assert.Contains(applied.Warnings, w => w.Contains("game may reject", StringComparison.Ordinal));
        var after = fixture.Bytes;
        AssertOnlyPlannedTokensChanged(before, after, preview.Plan);
        using var reopened = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("data.json", after));
        var fields = JsonEditingTests.Fields(reopened).ToDictionary(f => f.ValueId);
        foreach (var change in preview.Plan.Changes) Assert.Equal(change.NewValue, fields[change.ValueId].Value);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.True(before.AsSpan().SequenceEqual(fixture.Bytes), "One undo must restore every changed token exactly.");
        Assert.True(fixture.Session.Redo().Ok);
        Assert.True(after.AsSpan().SequenceEqual(fixture.Bytes));
        return preview.Plan;
    }

    private static void AssertOnlyPlannedTokensChanged(byte[] before, byte[] after, ActionPlan plan)
    {
        var bom = before.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
        var reader = new Utf8JsonReader(before.AsSpan(bom));
        var replacements = new List<(int Offset, int Length, byte[] New)>();
        var index = 0;
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.EndArray or JsonTokenType.EndObject) continue;
            var id = $"value/{index++}";
            var change = plan.Changes.SingleOrDefault(c => c.ValueId == id);
            if (change is null) continue;
            var value = change.NewValue.AsUnion().Value;
            var encoded = value.Kind switch
            {
                FieldValueKind.ExactNumber => Encoding.UTF8.GetBytes(value.AsExactNumber()),
                FieldValueKind.Bool => value.AsBool() ? "true"u8.ToArray() : "false"u8.ToArray(),
                _ => JsonSerializer.SerializeToUtf8Bytes(value.AsString()),
            };
            replacements.Add(((int)reader.TokenStartIndex + bom, (int)(reader.BytesConsumed - reader.TokenStartIndex), encoded));
        }
        Assert.Equal(plan.Changes.Count, replacements.Count);
        using var output = new MemoryStream();
        var previous = 0;
        foreach (var (offset, length, value) in replacements)
        {
            output.Write(before.AsSpan(previous, offset - previous));
            output.Write(value);
            previous = offset + length;
        }
        output.Write(before.AsSpan(previous));
        // F1 compares the output with an independently patched source; unknown bytes never enter logs.
        var diff = ByteDiffer.Compare(output.ToArray(), after);
        Assert.Equal(0, diff.ChangedBytes);
    }
}
