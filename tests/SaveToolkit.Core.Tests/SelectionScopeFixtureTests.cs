using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class SelectionScopeFixtureTests
{
    private static SelectionScopeCase Case => new("restore", "items", ["a"])
    {
        Stage = document => Assert.True(document.ApplyEdits([new("a/value", FieldValue.Of(2)), new("b/value", FieldValue.Of(3))]).Succeeded),
        Parameters = new Dictionary<string, FieldValue> { ["target"] = FieldValue.Of(0) },
        RowOwnedValueIds = new Dictionary<string, IReadOnlyList<string>> { ["a"] = ["a/value"], ["b"] = ["b/value"] },
        ExpectedChangedValueIds = ["a/value"],
        PermittedSharedValueIds = ["total"],
        PermittedDerivedValueIds = ["derived"],
    };

    [Fact]
    public void StagedSelectedRowAndExplicitSharedDerivedEffectsPassWithoutApplying()
    {
        var result = SelectionScopeFixture.Evaluate(() => new Document("good"), Case);
        Assert.Equal(SelectionScopeStatus.Passed, result.Status);
        Assert.Contains(result.Diagnostics, text => text.Contains("advisory", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("nondeterministic")]
    [InlineData("mutating")]
    [InlineData("missing-expected")]
    [InlineData("false-derived")]
    public void ProvenViolationsFail(string mode) =>
        Assert.Equal(SelectionScopeStatus.Failed, SelectionScopeFixture.Evaluate(() => new Document(mode), Case).Status);

    [Theory]
    [InlineData("unknown")]
    [InlineData("rejected")]
    [InlineData("noop")]
    [InlineData("unchanged")]
    [InlineData("missing-table")]
    [InlineData("missing-action")]
    [InlineData("unbound")]
    [InlineData("lazy-cut")]
    public void InconclusiveEvidenceNeverPasses(string mode) =>
        Assert.Equal(SelectionScopeStatus.NotEvaluated, SelectionScopeFixture.Evaluate(() => new Document(mode), Case,
            new SelectionScopeLimits { MaxLazyExpansions = 0 }).Status);

    [Fact]
    public void MissingExpectationsOrOwnershipNeverPass()
    {
        Assert.Equal(SelectionScopeStatus.NotEvaluated, SelectionScopeFixture.Evaluate(() => new Document("good"), Case with { ExpectedChangedValueIds = [] }).Status);
        Assert.Equal(SelectionScopeStatus.NotEvaluated, SelectionScopeFixture.Evaluate(() => new Document("good"), Case with { RowOwnedValueIds = new Dictionary<string, IReadOnlyList<string>>() }).Status);
    }

    [Fact]
    public void NodeAndDepthCutsNeverPass()
    {
        Assert.Equal(SelectionScopeStatus.NotEvaluated, SelectionScopeFixture.Evaluate(() => new Document("good"), Case, new() { MaxNodes = 1 }).Status);
        Assert.Equal(SelectionScopeStatus.NotEvaluated, SelectionScopeFixture.Evaluate(() => new Document("good"), Case, new() { MaxDepth = 0 }).Status);
    }

    [Fact]
    public void StageRejectionIsNotEvaluated()
    {
        var result = SelectionScopeFixture.Evaluate(() => new Document("good"), Case with { Stage = _ => throw new InvalidOperationException("cannot stage") });
        Assert.Equal(SelectionScopeStatus.NotEvaluated, result.Status);
    }

    private sealed class Document(string mode) : BoundSaveDocument, IActionPlanningDocument
    {
        private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal) { ["a/value"] = 1, ["b/value"] = 1 };
        private int _plans;
        public override string Title => "Synthetic selection fixture";
        public override ViewNode BuildView() => new SectionNode(Title,
        [
            new ActionNode("restore", "Restore") { SupportsPlanning = true, SelectionTableId = mode == "unbound" ? null : "items" },
            new TableNode([new("value", "Value")], _values.Select(pair => new TableRow(pair.Key[..1],
                new Dictionary<string, ViewNode> { ["value"] = Field(pair.Key, "Value", IntegerSpec.Int32, () => FieldValue.Of(_values[pair.Key]), v => _values[pair.Key] = v.AsInt32()) })).ToArray())
                { SelectionId = mode == "missing-table" ? null : "items" },
            mode == "lazy-cut" ? SectionNode.Lazy("Unvisited", "lazy") : new TextNode("Complete"),
        ]);
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        {
            Assert.Equal(["a"], scope.RowKeys);
            Assert.Equal(FieldValue.Of(0), parameters["target"]);
            Assert.Equal(3, _values["b/value"]);
            _plans++;
            if (mode == "missing-action") throw new NotSupportedException();
            if (mode == "rejected") return new() { Errors = [new("restore", "Rejected")] };
            if (mode == "noop") return new();
            if (mode == "unchanged") return new() { Changes = [new("a/value", "Value", FieldValue.Of(2), FieldValue.Of(2))] };
            if (mode == "mutating") _values["a/value"]++;
            var id = mode switch { "foreign" => "b/value", "unknown" => "unknown", "missing-expected" => "total", _ => "a/value" };
            var old = FieldValue.Of(_values["a/value"]);
            return new() { Changes = [new(id, "Value", old, mode == "unchanged" ? old : FieldValue.Of(mode == "nondeterministic" ? _plans : 0)),
                new("total", "Total", FieldValue.Of(5), FieldValue.Of(3)),
                new("derived", "Derived", FieldValue.Of(5), FieldValue.Of(3)) { Derived = mode != "false-derived" }] };
        }
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
            throw new InvalidOperationException("The advisory fixture must never apply a plan.");
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.bin", BitConverter.GetBytes(_values["a/value"]).Concat(BitConverter.GetBytes(_values["b/value"])).ToArray())];
    }
}
