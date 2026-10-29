using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class PointTargetActionTests
{
    internal const string ActionId = "progression/set-point-targets";

    [Fact]
    public void PreviewCancelAndDirectInvocationLeaveEverySampleUnchanged()
    {
        foreach (var file in ProgressionSampleTests.Files())
        {
            var plugin = new Cyberpunk2077Plugin();
            using var document = plugin.Open(file);
            using var session = new DocumentSession("point-cancel", (DiskSaveFile)file, plugin, document);
            var tab = Assert.Single(Assert.IsType<TabsNode>(session.BuildView()).Tabs, t => t.Title == "Player development");
            var action = Assert.Single(tab.Children.OfType<ActionNode>(), a => a.ActionId == ActionId);
            Assert.True(action.SupportsPlanning);
            Assert.Contains("Inferred", action.Description);
            Assert.Contains(WriteSafety.Warning, action.Description);
            Assert.Equal(3, action.Parameters.Count);
            foreach (var parameter in action.Parameters)
            {
                var field = Assert.Single(tab.Children.OfType<FieldNode>(), f => f.ValueId == $"progression/points/{parameter.ValueId}");
                Assert.Equal(field.Spec, parameter.Spec);
                Assert.Equal(field.Value, parameter.Value);
                Assert.Equal(Confidence.Inferred, parameter.Confidence);
            }
            var preview = session.PlanAction(ActionId, Targets(12, 9, 3), new());
            Assert.NotNull(preview.PlanId);
            Assert.Empty(preview.Plan.Errors);
            Assert.Equal(3, preview.Plan.Changes.Count);
            Assert.Contains(preview.Plan.SkippedRows, row => row.RowKey == "Secondary");
            Assert.Contains(preview.Plan.Warnings, w => w.StartsWith(WriteSafety.Warning, StringComparison.Ordinal));
            Assert.Contains(preview.Plan.Warnings, w => w.Contains("independent", StringComparison.Ordinal));
            foreach (var change in preview.Plan.Changes)
            {
                Assert.Equal(document.GetValue(change.ValueId), change.OldValue);
                Assert.Equal(TargetValues()[change.ValueId["progression/points/".Length..]], change.NewValue);
                Assert.False(change.Derived);
                Assert.True(change.OldExists);
                Assert.True(change.NewExists);
                Assert.Contains("Unspent", change.Label);
            }
            // Cancel closes the review without applying its token; a replacement preview also stays pure.
            Assert.NotNull(session.PlanAction(ActionId, Targets(7, 0, 0), new()).PlanId);
            Assert.False(session.InvokeAction(ActionId, Targets(12, 9, 3)).Ok);
            Assert.Empty(document.InvokeAction(ActionId, TargetValues()).Changes);
            Assert.False(session.State.CanUndo);
            Assert.False(session.State.IsDirty);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void BatchAppliesAndUndoesAsOneStepWithOnlyUnspentPropertyChanges()
    {
        foreach (var file in ProgressionSampleTests.Files())
        {
            var original = CsavContainer.Parse(file.ReadAllBytes());
            var plugin = new Cyberpunk2077Plugin();
            using var document = plugin.Open(file);
            using var session = new DocumentSession("point-apply", (DiskSaveFile)file, plugin, document);
            _ = session.BuildView();
            var preview = session.PlanAction(ActionId, Targets(12, 9, 3), new());
            Assert.NotNull(preview.PlanId);
            Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
            var bytes = Assert.Single(session.Serialize()).Content.ToArray();
            PointTargetFixtures.AssertOnlyUnspentChanges(original, CsavContainer.Parse(bytes), TargetValues());
            using var reopened = ProgressionSampleTests.Open(bytes);
            _ = reopened.BuildView();
            foreach (var (type, target) in TargetValues())
                Assert.Equal(target, reopened.GetValue($"progression/points/{type}"));
            Assert.True(session.Undo().Ok);
            Assert.False(session.State.CanUndo);
            Assert.False(session.State.IsDirty);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(bytes, Assert.Single(session.Serialize()).Content.ToArray());
        }
    }

    internal static Dictionary<string, FieldValue> TargetValues() => new()
    {
        ["Attribute"] = FieldValue.Of(12L), ["Primary"] = FieldValue.Of(9L), ["Espionage"] = FieldValue.Of(3L),
    };

    internal static Dictionary<string, JsonElement> Targets(int attribute, int perk, int relic) => new()
    {
        ["Attribute"] = JsonSerializer.SerializeToElement(attribute.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["Primary"] = JsonSerializer.SerializeToElement(perk.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["Espionage"] = JsonSerializer.SerializeToElement(relic.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    };
}
