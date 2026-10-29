using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class InventoryEligibilityTests
{
    private const string HeldId = "slots/0/inventory/held";
    private const string RefillId = "slots/0/inventory/refill";
    private static readonly Dictionary<string, FieldValue> NoParameters = [];

    [Theory]
    [InlineData("empty", "All-zero vacant slot")]
    [InlineData("unknown-id", "Exact item ID is outside the two-ID")]
    [InlineData("container-candidate", "Exact item ID is outside the two-ID")]
    [InlineData("unknown-handle", "Item identity is unresolved")]
    [InlineData("missing-instance", "missing or ambiguous instance handle")]
    [InlineData("weapon", "Resolved category 'weapon'")]
    [InlineData("talisman", "Resolved category 'talisman'")]
    [InlineData("key", "Serialized array 'key' is outside common-array quantity editing")]
    [InlineData("zero", "Imported quantity is zero")]
    [InlineData("above-ceiling", "Imported quantity 13 exceeds the conservative observed ceiling 12")]
    [InlineData("duplicate", "Handle is repeated within this container")]
    [InlineData("count-mismatch", "held/common: declared 2, observed 1")]
    [InlineData("other-array-count-mismatch", "held/key: declared 1, observed 0")]
    public void ProtectedRowsExplainActualRestrictionInFieldsDetailsSearchAndRefill(string scenario, string reason)
    {
        var fixture = ProtectedFixture(scenario);
        using var document = fixture.Save.Open();
        var queryable = (IQueryableDocument)document;
        var page = queryable.Query(HeldId, new() { Text = reason, IncludeEmpty = true, Limit = 1 });
        var row = Assert.Single(page.Rows);
        Assert.Equal("Protected", Assert.IsType<TextNode>(row.Cells["editing"]).Text);
        var quantity = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.True(quantity.Spec.ReadOnly);
        Assert.Equal(Confidence.Unknown, quantity.Confidence);
        Assert.Contains(reason, quantity.Description);
        var exactReason = scenario switch
        {
            "empty" => "All-zero vacant slot; quantity editing cannot insert a record.",
            "key" => "Serialized array 'key' is outside common-array quantity editing.",
            "zero" => "Imported quantity is zero; quantity editing cannot create or remove a stack.",
            "above-ceiling" => "Imported quantity 13 exceeds the conservative observed ceiling 12.",
            "duplicate" => "Handle is repeated within this container; quantity editing requires a unique handle.",
            "unknown-id" or "container-candidate" => "Exact item ID is outside the two-ID observed-ceiling allowlist; its quantity/dependency rules are unverified.",
            _ => null,
        };
        if (exactReason is not null) Assert.Equal("Protected: " + exactReason, quantity.Description);
        var details = Assert.IsType<SectionNode>(row.Cells["details"]);
        Assert.Equal(quantity.Description, Assert.IsType<TextNode>(details.Children[0]).Text);
        var plan = ((IActionPlanningDocument)document).PlanAction(RefillId, NoParameters, new([row.Key]));
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        var skipped = Assert.Single(plan.SkippedRows);
        Assert.Equal(row.Key, skipped.RowKey);
        Assert.Equal(quantity.Description, skipped.Reason);
        Assert.False(document.ApplyEdits([new(quantity.ValueId, FieldValue.Of(2L))]).Succeeded);
        var item = Assert.IsType<FieldNode>(row.Cells["item"]);
        Assert.True(item.Spec.ReadOnly);
        Assert.Equal(item.Value, document.GetValue(item.ValueId));
        // A named Cracked Pot is protected by the finite allowlist, not an inferred inventory role.
        if (scenario == "container-candidate") Assert.DoesNotContain("container-managed", quantity.Description);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }

    [Theory]
    [InlineData("held", 0xB00050F0U, "0x400050F0", 12U)]
    [InlineData("held", 0xB0002774U, "0x40002774", 3U)]
    [InlineData("storage", 0xB00050F0U, "0x400050F0", 12U)]
    [InlineData("storage", 0xB0002774U, "0x40002774", 3U)]
    public void ObservedCeilingIsEditableAndRemainsFixedAfterEditAndReopen(string container, uint handle, string rawId, uint ceiling)
    {
        var fixture = new SemanticFixture();
        fixture.Row(container, "common", 1800, handle, ceiling, 77);
        using var document = fixture.Save.Open();
        var queryable = (IQueryableDocument)document;
        var id = "slots/0/inventory/" + container;
        var row = Assert.Single(queryable.Query(id, new() { Text = "editable QUANTITY" }).Rows);
        Assert.Equal("Editable quantity", Assert.IsType<TextNode>(row.Cells["editing"]).Text);
        var quantity = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.False(quantity.Spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, quantity.Confidence);
        Assert.Equal((Int128)1, Assert.IsType<IntegerSpec>(quantity.Spec).Min);
        Assert.Equal((Int128)ceiling, Assert.IsType<IntegerSpec>(quantity.Spec).Max);
        Assert.Contains($"1–{ceiling}", quantity.Description);
        Assert.Contains("may reject", quantity.Warning);
        Assert.Equal(rawId, Assert.IsType<FieldNode>(row.Cells["item"]).Value.AsString());
        var planner = (IActionPlanningDocument)document;
        var atCeiling = planner.PlanAction(RefillId, NoParameters, new([row.Key]));
        Assert.Empty(atCeiling.Changes);
        Assert.Equal("Already at the conservative observed ceiling.", Assert.Single(atCeiling.SkippedRows).Reason);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
        var outcome = document.ApplyEdits([new(quantity.ValueId, FieldValue.Of(1L))]);
        Assert.True(outcome.Succeeded);
        var current = Assert.Single(queryable.Query(id, new() { Text = "Eligibility and ceiling are fixed" }).Rows);
        Assert.Equal(row.Key, current.Key);
        Assert.Equal(quantity.Description, Assert.IsType<FieldNode>(current.Cells["quantity"]).Description);
        Assert.Equal((Int128)ceiling, Assert.IsType<IntegerSpec>(Assert.IsType<FieldNode>(current.Cells["quantity"]).Spec).Max);
        Assert.Equal(ceiling, Assert.Single(planner.PlanAction(RefillId, NoParameters, new([row.Key])).Changes).NewValue.AsUInt32());
        using var reopened = WriteTestSupport.Reopen(document);
        var reopenedRow = Assert.Single(((IQueryableDocument)reopened).Query(id, new() { Text = rawId }).Rows);
        Assert.Equal(row.Key, reopenedRow.Key);
        Assert.Equal(quantity.Description, Assert.IsType<FieldNode>(reopenedRow.Cells["quantity"]).Description);
        Assert.True(document.ApplyEdits(outcome.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }

    [Fact]
    public void ReadOnlyProfileVersionIsDistinctFromItemEligibilityWithoutInterpretingUnknownSlotVersions()
    {
        var fixture = new SemanticFixture();
        fixture.Row("held", "common", 0, 0xB00050F0, 1, 1);
        fixture.Row("held", "common", 1, 0xB000FFFF, 1, 2);
        fixture.Save.Write32(SyntheticSave.UserData, 91);
        using var document = fixture.Save.Open();
        var queryable = (IQueryableDocument)document;
        var rows = queryable.Query(HeldId, new() { Filters = new Dictionary<string, string> { ["editing"] = "Read-only version" } }).Rows;
        Assert.Equal(2, rows.Count);
        foreach (var row in rows)
        {
            var quantity = Assert.IsType<FieldNode>(row.Cells["quantity"]);
            Assert.True(quantity.Spec.ReadOnly);
            Assert.Contains("version has not been verified for writing", quantity.Description);
            Assert.False(document.ApplyEdits([new(quantity.ValueId, FieldValue.Of(2L))]).Succeeded);
        }
        var supportedRow = rows.Single(row => Assert.IsType<FieldNode>(row.Cells["item"]).Value.AsString() == "0x400050F0");
        var supported = Assert.IsType<FieldNode>(supportedRow.Cells["quantity"]);
        Assert.Equal(Confidence.Inferred, supported.Confidence);
        Assert.Contains("Observed quantity range: 1–12", supported.Description);
        Assert.Contains("outside the two-ID", Assert.IsType<FieldNode>(rows.Single(row => row.Key != supportedRow.Key).Cells["quantity"]).Description);
        Assert.Empty(queryable.Query(HeldId, new() { Text = "Editable quantity" }).Rows);
        Assert.NotEmpty(((IActionPlanningDocument)document).PlanAction(RefillId, NoParameters, new()).Errors);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);

        var unknownSlot = new SyntheticSave([0], version: 151);
        using var unknownDocument = unknownSlot.Open();
        Assert.DoesNotContain(((IQueryableDocument)unknownDocument).Collections, collection => collection.Id == HeldId);
        WriteTestSupport.EqualBytes(unknownSlot.Bytes, unknownDocument);
    }

    private static SemanticFixture ProtectedFixture(string scenario)
    {
        var fixture = new SemanticFixture();
        if (scenario == "empty") return fixture;
        var handle = scenario switch
        {
            "unknown-id" => 0xB000FFFFU, "container-candidate" => 0xB000251CU,
            "unknown-handle" => uint.MaxValue, "missing-instance" => 0x8001FFFFU,
            "weapon" => 0x80000001U, "talisman" => 0xA0001000U, _ => 0xB00050F0U,
        };
        if (scenario == "weapon") fixture.Instance(1, handle, 0x00001000);
        fixture.Row("held", scenario == "key" ? "key" : "common", 0, handle,
            scenario == "zero" ? 0U : scenario == "above-ceiling" ? 13U : 1U, 1);
        if (scenario == "duplicate") fixture.Row("held", "common", 1, handle, 2, 2);
        if (scenario == "count-mismatch") fixture.Save.Write32(fixture.Held, 2);
        if (scenario == "other-array-count-mismatch")
            fixture.Save.Write32(fixture.Held + sizeof(uint) + SemanticLayout.HeldCommonCapacity * 3 * sizeof(uint), 1);
        return fixture;
    }
}
