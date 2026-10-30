using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Core.Tests.Fakes;
using System.Text.Json;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectR2AdmissionTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void NearlyFullRequestSharesInclusiveWireAllowanceWithPlanAndUnplannedCanonicalPrimary(int excess, bool accepted)
    {
        const int maximum = 4 * 1024 * 1024;
        using var document = new ClosureDocument();
        var fixedBytes = BoundedJson.Measure(Plan(), maximum) + BoundedJson.Measure(new ActionScope(), maximum)
            + BoundedJson.Measure(new Dictionary<string, FieldValue> { ["payload"] = FieldValue.Of("") }, maximum)
            + BoundedJson.Measure(document.Descriptors(), maximum);
        document.Payload = new string('x', checked((int)(maximum - fixedBytes + excess)));
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();

        var preview = fixture.Session.PlanAction("plan", new Dictionary<string, JsonElement>());

        Assert.Equal(accepted, preview.PlanId is not null);
        Assert.Equal(accepted ? 3 : 0, document.GetterCalls);
        Assert.Equal(2, document.ResolverCalls);
        if (!accepted) Assert.Contains(preview.Plan.Errors, error => error.Message.Contains("large", StringComparison.Ordinal));
    }

    [Fact]
    public void EntireCanonicalDependencyClosureMustFitBeforeAnyGetterOrWarmPublication()
    {
        using var document = new ClosureDocument();
        var index = new ViewIndex(document);
        index.Add(document.BuildView());
        var plan = Plan();
        var original = FieldMetadataPlanning.Validate(index, document, plan);
        Assert.Equal(["alias", "primary"], original.Select(field => field.ValueId));
        document.GetterCalls = 0;
        document.PrimaryLabel = new string('p', 100_000);
        var exact = BoundedJson.Measure(document.Descriptors(), 4 * 1024 * 1024);

        Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document, plan,
            null, CancellationToken.None, exact - 1));

        Assert.Equal(0, document.GetterCalls);
        Assert.Equal("Primary", index.ResolvedMetadata("primary")!.Label);
        var accepted = FieldMetadataPlanning.Validate(index, document, plan, null, CancellationToken.None, exact);
        Assert.Equal(exact, BoundedJson.Measure(accepted, 4 * 1024 * 1024));
        Assert.Equal(document.PrimaryLabel, accepted[1].Label);
        Assert.Equal(3, document.GetterCalls); // Complete closure plus the genuine old-value check.
    }

    [Fact]
    public void AggregateLaterBatchOverflowHasNoEarlierGetterOrPublishedMetadata()
    {
        using var document = new ClosureDocument();
        var index = new ViewIndex(document);
        index.Add(document.BuildView());
        document.PrimaryLabel = new string('p', 20_000);
        var aliasOnly = BoundedJson.Measure(document.Descriptors().Take(1).ToArray(), 1000);
        Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document, Plan(),
            null, CancellationToken.None, aliasOnly + 100));
        Assert.Equal(0, document.GetterCalls);
        Assert.Null(index.ResolvedMetadata("alias"));
        Assert.Null(index.ResolvedMetadata("primary"));
        Assert.Equal(2, document.ResolverCalls);
    }

    private static ActionPlan Plan() => new()
    {
        Changes = [new PlannedChange("alias", "Alias", FieldValue.Of(1), FieldValue.Of(2)) { Derived = true }],
    };

    private sealed class ClosureDocument : BoundSaveDocument, IFieldMetadataDocument, IActionPlanningDocument
    {
        public int GetterCalls { get; set; }
        public int ResolverCalls { get; private set; }
        public string PrimaryLabel { get; set; } = "Primary";
        public string Payload { get; set; } = "";
        public override string Title => "Closure";
        public ClosureDocument()
        {
            RegisterField("alias", IntegerSpec.Int32 with { ReadOnly = true }, () => { GetterCalls++; return FieldValue.Of(1); });
            RegisterField("primary", IntegerSpec.Int32, () => { GetterCalls++; return FieldValue.Of(1); });
        }
        public IReadOnlyList<FieldMetadata> Descriptors() =>
        [
            new("alias", "Alias", IntegerSpec.Int32 with { ReadOnly = true }) { BindingKind = FieldBindingKind.Derived, CanonicalValueId = "primary" },
            new("primary", PrimaryLabel, IntegerSpec.Int32) { CanonicalValueId = "primary" },
        ];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids)
        {
            ResolverCalls++;
            return Descriptors().Where(field => ids.Contains(field.ValueId, StringComparer.Ordinal)).ToArray();
        }
        public override ViewNode BuildView() => new ActionNode("plan", "Plan")
        {
            SupportsPlanning = true,
            Parameters = [new("payload", "Payload", new TextSpec(), FieldValue.Of(Payload))],
        };
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => Plan();
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) => new();
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
