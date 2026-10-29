using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseActionCharacterizationTests
{
    private const string ActionId = "inventory/set-quantity";
    private const string FirstRow = "inventory/0/quantity";
    private const string SecondRow = "inventory/1/quantity";
    private const string PlanWarning = "Experimental. Not validated on console. The game may reject or overwrite the edited save. " +
        "The PS5 item mapping and 15-count stack bound are inferred from existing samples and PS4 research; console loading, later acquisition and zero-count semantics are unvalidated. " +
        "Only existing recognised records change. Ciphertext SHA1 and encryption are recomputed and self-checked; the game may still reject the edited save.";
    private const string ApplyWarning = "Experimental. Not validated on console. The game may reject or overwrite the edited save. " +
        "PS5 field mapping is inferred from PS4 research and sample bytes; edits have not been tested in-game. The game may reject an edited save even though ciphertext SHA1 and encryption self-check succeed.";

    [Fact]
    public void ExplicitPlan_PreservesSelectionOrderLabelsSkipReasonsAndMergedWarnings()
    {
        using var document = Open();
        _ = document.ExpandNode("inventory");
        var before = Bytes(document);
        var scope = new ActionScope([SecondRow, "inventory/4/quantity", "inventory/3/quantity", "inventory/2/quantity", FirstRow]);
        var plan = document.PlanAction(ActionId, Parameters(), scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(new[]
        {
            new PlannedChange(SecondRow, "Peach Gel · quantity (raw ID 0x0002)", FieldValue.Of(15L), FieldValue.Of(12L)),
            new PlannedChange(FirstRow, "Apple Gel · quantity (raw ID 0x0001)", FieldValue.Of(1L), FieldValue.Of(12L)),
        }, plan.Changes);
        Assert.Equal(new[]
        {
            new SkippedActionRow("inventory/4/quantity", "Empty slot; no item is inserted or changed."),
            new SkippedActionRow("inventory/3/quantity", "Numeric ID is not one of the recognised consumable mappings; preserved."),
            new SkippedActionRow("inventory/2/quantity", "Existing quantity 16 exceeds the inferred 15 bound; preserved."),
        }, plan.SkippedRows);
        Assert.Equal(new[] { PlanWarning, SelectionWarning("Explicit selection contains 5 existing inventory rows.") }, plan.Warnings);
        Assert.Equal(before, Bytes(document));

        var outcome = document.ApplyActionPlan(ActionId, Parameters(), scope, plan);
        Assert.True(outcome.Succeeded);
        Assert.Equal(plan.Changes.Select(change => new ValueChange(change.ValueId, change.OldValue, change.NewValue)), outcome.Changes);
        Assert.Equal(plan.Warnings.Append(ApplyWarning), outcome.Warnings);
    }

    [Fact]
    public void EmptySelectionAndAlreadyEqualQuantity_KeepOnlyPlanWarnings()
    {
        using var document = Open();
        _ = document.ExpandNode("inventory");
        var before = Bytes(document);
        var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(15L) };
        foreach (var scope in new[] { new ActionScope([]), new ActionScope([SecondRow]) })
        {
            var plan = document.PlanAction(ActionId, parameters, scope);
            Assert.Empty(plan.Errors);
            Assert.Empty(plan.Changes);
            Assert.Equal(new[] { PlanWarning, SelectionWarning($"Explicit selection contains {scope.RowKeys!.Count} existing inventory rows.") }, plan.Warnings);
            if (scope.RowKeys.Count > 0)
                Assert.Equal(new SkippedActionRow(SecondRow, "Existing quantity already equals the requested value."), Assert.Single(plan.SkippedRows));
            else
                Assert.Empty(plan.SkippedRows);
            var result = document.ApplyActionPlan(ActionId, parameters, scope, plan);
            Assert.True(result.Succeeded);
            Assert.Empty(result.Changes);
            Assert.Equal(plan.Warnings, result.Warnings);
        }
        Assert.Equal(before, Bytes(document));
    }

    [Theory]
    [InlineData("change-order")]
    [InlineData("label")]
    [InlineData("old-value")]
    [InlineData("new-value")]
    [InlineData("derived")]
    [InlineData("old-exists")]
    [InlineData("new-exists")]
    [InlineData("skip-order")]
    [InlineData("skip-reason")]
    [InlineData("warning-order")]
    [InlineData("errors")]
    public void ChangedPreview_IsRejectedWithFreshWarningsAndNoWrite(string mutation)
    {
        using var document = Open();
        _ = document.ExpandNode("inventory");
        var scope = new ActionScope();
        var fresh = document.PlanAction(ActionId, Parameters(), scope);
        var first = fresh.Changes[0];
        var reviewed = mutation switch
        {
            "change-order" => fresh with { Changes = fresh.Changes.Reverse().ToArray() },
            "label" => Change(first with { Label = "Different label" }),
            "old-value" => Change(first with { OldValue = FieldValue.Of(2L) }),
            "new-value" => Change(first with { NewValue = FieldValue.Of(13L) }),
            "derived" => Change(first with { Derived = true }),
            "old-exists" => Change(first with { OldExists = false }),
            "new-exists" => Change(first with { NewExists = false }),
            "skip-order" => fresh with { SkippedRows = fresh.SkippedRows.Reverse().ToArray() },
            "skip-reason" => fresh with { SkippedRows = [fresh.SkippedRows[0] with { Reason = "Different reason" }, .. fresh.SkippedRows.Skip(1)] },
            "warning-order" => fresh with { Warnings = fresh.Warnings.Reverse().ToArray() },
            "errors" => fresh with { Errors = [new(ActionId, "Unreviewable preview")] },
            _ => throw new ArgumentException("Unknown mutation.", nameof(mutation)),
        };
        var before = Bytes(document);
        var result = document.ApplyActionPlan(ActionId, Parameters(), scope, reviewed);
        Assert.False(result.Succeeded);
        Assert.Equal(new FieldError(ActionId, "The quantity preview changed; preview the action again."), Assert.Single(result.Errors));
        Assert.Equal(new[] { PlanWarning, SelectionWarning("Document scope selected 4 existing inventory rows.") }, result.Warnings);
        Assert.Empty(result.Changes);
        Assert.Equal(before, Bytes(document));

        ActionPlan Change(PlannedChange change) => fresh with { Changes = [change, .. fresh.Changes.Skip(1)] };
    }

    [Fact]
    public void FreshValidationErrors_TakePrecedenceOverChangedPreview()
    {
        using var document = Open();
        _ = document.ExpandNode("inventory");
        var reviewed = document.PlanAction(ActionId, Parameters(), new ActionScope([FirstRow]));
        var invalid = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(16L) };
        var before = Bytes(document);
        var result = document.ApplyActionPlan(ActionId, invalid, new ActionScope([FirstRow]), reviewed with { Warnings = [] });
        Assert.Equal(new FieldError("quantity", "Quantity must be a whole number from 0 to 15."), Assert.Single(result.Errors));
        Assert.Equal(new[] { PlanWarning }, result.Warnings);
        Assert.Empty(result.Changes);
        Assert.Equal(before, Bytes(document));
    }

    [Fact]
    public void QueryScope_BindsUnloadedRowsOnlyWhenApplying()
    {
        using var document = Open();
        var scope = new ActionScope([SecondRow, FirstRow]) { QueryScope = new("inventory", new QueryableCollectionQuery(), 0) };
        var before = Bytes(document);
        var plan = document.PlanAction(ActionId, Parameters(), scope);
        Assert.Empty(plan.Errors);
        Assert.Throws<KeyNotFoundException>(() => document.GetValue(FirstRow));
        Assert.Equal(before, Bytes(document));
        var result = document.ApplyActionPlan(ActionId, Parameters(), scope, plan);
        Assert.True(result.Succeeded);
        Assert.Equal(12, document.GetValue(FirstRow).AsInt64());
        Assert.Equal(plan.Warnings.Append(ApplyWarning), result.Warnings);
    }

    private static string SelectionWarning(string selection) => selection + " No IDs, flags, order or records are added or removed.";
    private static Dictionary<string, FieldValue> Parameters() => new() { ["quantity"] = FieldValue.Of(12L) };
    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
    private static AriseDocument Open() => Assert.IsType<AriseDocument>(new TalesOfArisePlugin().Open(
        MemorySaveSet.SingleFile(AriseTestContainer.FileName, new AriseTestContainer().Encrypt(), AriseTestContainer.TitleId)));
}
