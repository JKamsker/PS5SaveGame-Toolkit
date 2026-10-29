using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherGwentPlanningBoundaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void PlansUseCurrentValuesAndPerFieldImportedBoundsWhileSkippingLeadersAndUnknowns(int target)
    {
        var catalog = ReferencePackFixture.Pin().Catalog("gwent");
        GwentCopies[] copies = [new(new(108, 1)), new(new(109, 5)), new(new(1001, 1) { Leader = true }),
            new(new(999999, 1)), new(new(1002, 1))]; // Catalog leader in ordinary group must also stay untouched.
        copies[1].Value = 0;
        var plan = new GwentPlanning(copies, catalog, true).Plan(WitcherGwentPlanningTests.Parameters(target), new());
        Assert.Empty(plan.Errors);
        Assert.Equal([1, 0, 1, 1, 1], copies.Select(c => c.Value));
        Assert.Equal(5, copies[1].Maximum); // Current reduction must not erase opening-value bounds.
        Assert.Equal(target is 0 or 1 or 5 ? 1 : 2, plan.Changes.Count);
        Assert.Contains(plan.SkippedRows, s => s.RowKey == "1001");
        Assert.Contains(plan.SkippedRows, s => s.RowKey == "1002");
        Assert.Contains(plan.SkippedRows, s => s.RowKey == "999999");
        Assert.All(plan.SkippedRows, s => Assert.NotEmpty(s.Reason));
        Assert.All(plan.Changes, c => Assert.Equal(FieldValue.Of(target), c.NewValue));
        if (target != 0)
        {
            var high = Assert.Single(plan.Changes, c => c.ValueId == copies[1].ValueId);
            Assert.Equal(FieldValue.Of(0), high.OldValue);
        }
    }

    [Fact]
    public void UnsupportedDocumentsMissingReferencesAndInvalidParametersNeverWrite()
    {
        GwentCopies[] copies = [new(new(108, 1))];
        var catalog = ReferencePackFixture.Pin().Catalog("gwent");
        var locked = new GwentPlanning(copies, catalog, false).Plan(WitcherGwentPlanningTests.Parameters(3), new());
        Assert.NotEmpty(locked.Errors);
        Assert.Empty(locked.Changes);
        var missing = new GwentPlanning(copies, null, true).Plan(WitcherGwentPlanningTests.Parameters(3), new());
        Assert.Empty(missing.Changes);
        Assert.Single(missing.SkippedRows);
        foreach (var parameters in new[] {
            new Dictionary<string, FieldValue>(), WitcherGwentPlanningTests.Parameters(-1),
            WitcherGwentPlanningTests.Parameters(4), WitcherGwentPlanningTests.Parameters(int.MaxValue),
            new Dictionary<string, FieldValue> { [GwentPlanning.CopiesParameter] = FieldValue.Of(1.5) },
            new Dictionary<string, FieldValue> { [GwentPlanning.CopiesParameter] = FieldValue.Of("1") },
            new Dictionary<string, FieldValue> { [GwentPlanning.CopiesParameter] = FieldValue.Of(1), ["extra"] = FieldValue.Of(1) },
        })
        {
            var plan = new GwentPlanning(copies, catalog, true).Plan(parameters, new());
            Assert.NotEmpty(plan.Errors);
            Assert.Empty(plan.Changes);
        }
        Assert.Equal(1, copies[0].Value);
    }
}
