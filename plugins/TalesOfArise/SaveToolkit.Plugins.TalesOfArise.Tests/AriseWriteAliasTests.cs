using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseWriteAliasTests
{
    private const string QuantityId = "inventory/24/quantity";
    private const string ActionId = "inventory/set-quantity";
    private readonly TalesOfArisePlugin _plugin = new();

    [Theory]
    [InlineData("level", 4, 6)]
    [InlineData("hp", 8, 300)]
    [InlineData("sp", 0x30, 100)]
    public void ScalarEditAndHistory_ReportSheetExplorerAndEvidenceChanges(string scalar, int relative, long target)
    {
        var file = Sample();
        using var document = _plugin.Open(file);
        using var session = new DocumentSession("arise-alias-history", file, _plugin, document);
        _ = session.BuildView();
        var save = AriseFormat.Decode(file.ReadAllBytes());
        var sectionIndex = save.Sections.ToList().FindIndex(section => section.Name == "ArisePCStatus_000");
        var section = save.Sections[sectionIndex];
        _ = session.Expand($"section/{sectionIndex}");
        _ = session.Expand($"page/{sectionIndex}/0");
        var evidenceId = $"evidence/{section.Id:X8}/{relative:X6}";
        _ = session.QueryCollection($"evidence/{section.Id:X8}", new() { Offset = relative / sizeof(uint), Limit = 1 });
        string[] ids = [$"character/0/{scalar}", $"sheet/0/{scalar}", $"raw/{section.Offset + relative:X}", evidenceId];
        var original = document.GetValue(ids[0]);

        var applied = session.ApplyEdits([new(ids[0], JsonSerializer.SerializeToElement(target.ToString()))]);
        Assert.True(applied.Ok);
        AssertChanges(applied, ids, original, FieldValue.Of(target));
        AssertChanges(session.Undo(), ids, FieldValue.Of(target), original);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        AssertChanges(session.Redo(), ids, original, FieldValue.Of(target));
        Assert.Equal(FieldValue.Of(target), document.GetValue(evidenceId));
    }

    [Fact]
    public void QuantityPlan_ReportsExposedPackedWordAsDerived_AndHistoryDoesNotWriteIt()
    {
        var file = Sample();
        using var document = _plugin.Open(file);
        using var session = new DocumentSession("arise-plan-alias", file, _plugin, document);
        _ = session.BuildView();
        _ = session.Expand("inventory");
        var save = AriseFormat.Decode(file.ReadAllBytes());
        var sectionIndex = save.Sections.ToList().FindIndex(section => section.Name == "SaveData_ItemManager");
        var wordOffset = save.Sections[sectionIndex].Offset + 24 * 16;
        _ = session.Expand($"section/{sectionIndex}");
        _ = session.Expand($"page/{sectionIndex}/0");
        var rawId = $"raw/{wordOffset:X}";
        var oldWord = document.GetValue(rawId);
        var newWord = FieldValue.Of((long)((oldWord.AsUInt32() & ~0xFFu) | 12u));
        var parameters = new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("12") };
        var before = Assert.Single(document.Serialize()).Content.ToArray();

        var preview = session.PlanAction(ActionId, parameters, new ActionScope([QuantityId]));
        Assert.NotNull(preview.PlanId);
        var derived = Assert.Single(preview.Plan.Changes, change => change.ValueId == rawId);
        Assert.True(derived.Derived);
        Assert.Equal(oldWord, derived.OldValue);
        Assert.Equal(newWord, derived.NewValue);
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());

        var applied = session.ApplyActionPlan(preview.PlanId!, preview.Revision);
        AssertChanges(applied, [rawId], oldWord, newWord);
        Assert.Equal(preview.Plan.Changes.Select(change => new ValueChange(change.ValueId, change.OldValue, change.NewValue)), applied.Changes);
        Assert.Equal(12, document.GetValue(QuantityId).AsInt64());
        AssertChanges(session.Undo(), [rawId], newWord, oldWord);
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.CanUndo);
        AssertChanges(session.Redo(), [rawId], oldWord, newWord);
    }

    [Theory]
    [InlineData("effects")]
    [InlineData("warnings")]
    [InlineData("parameters")]
    [InlineData("stale")]
    public void QuantityPlan_RejectsChangedPreviewWithoutApplyingAnyOfItsEffects(string mutation)
    {
        var file = Sample();
        using var document = _plugin.Open(file);
        _ = document.BuildView();
        _ = document.LoadInventory();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var scope = new ActionScope([QuantityId]);
        var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(12L) };
        var plan = planner.PlanAction(ActionId, parameters, scope);
        Assert.Empty(plan.Errors);
        switch (mutation)
        {
            case "effects": plan = plan with { Changes = [] }; break;
            case "warnings": plan = plan with { Warnings = [] }; break;
            case "parameters": parameters["quantity"] = FieldValue.Of(13L); break;
            case "stale":
                Assert.True(document.ApplyEdits([new(QuantityId, FieldValue.Of(2L))]).Succeeded);
                break;
        }

        var before = Assert.Single(document.Serialize()).Content.ToArray();
        var outcome = planner.ApplyActionPlan(ActionId, parameters, scope, plan);
        Assert.False(outcome.Succeeded);
        Assert.Empty(outcome.Changes);
        Assert.NotEmpty(outcome.Warnings);
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void RejectedBatch_ReportsNoAliasChanges_AndPreservesBytes()
    {
        var file = Sample();
        using var document = _plugin.Open(file);
        _ = document.BuildView();
        var outcome = document.ApplyEdits([new("character/0/hp", FieldValue.Of(300L)), new("character/0/level", FieldValue.Of(101L))]);
        Assert.False(outcome.Succeeded);
        Assert.Empty(outcome.Changes);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static void AssertChanges(SessionEditResult result, IReadOnlyList<string> ids, FieldValue oldValue, FieldValue newValue)
    {
        Assert.True(result.Ok);
        foreach (var id in ids)
        {
            var change = Assert.Single(result.Changes, change => change.ValueId == id);
            Assert.Equal(oldValue, change.OldValue);
            Assert.Equal(newValue, change.NewValue);
        }
    }

    private static SaveToolkit.Core.Storage.DiskSaveFile Sample() =>
        Assert.IsType<SaveToolkit.Core.Storage.DiskSaveFile>(AriseSampleTests.GameplayFiles()
            .Single(file => file.FileName == "SaveData_002.sav" && !file.Directory.IsBackup));
}
