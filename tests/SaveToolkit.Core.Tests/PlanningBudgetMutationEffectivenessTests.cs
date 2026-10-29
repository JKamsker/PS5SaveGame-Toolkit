using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class PlanningBudgetMutationEffectivenessTests
{
    private static readonly Dictionary<string, JsonElement> NoParameters = [];

    [Fact]
    public void MetadataResolutionFailuresRejectEditsWithoutChangingValues()
    {
        var document = new MetadataPlanner { FailMetadata = true };
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        var result = fixture.Session.ApplyEdits([new("value", JsonSerializer.SerializeToElement(2))]);
        Assert.False(result.Ok);
        Assert.Equal(FieldValue.Of(1L), document.GetValue("value"));
        Assert.False(result.State.CanUndo);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void RetainedMetadataAndRequestBytesShareTheInclusivePlanBudget(int excess, bool accepted)
    {
        const int maximum = 4 * 1024 * 1024;
        var document = new MetadataPlanner();
        var planBytes = JsonSerializer.SerializeToUtf8Bytes(document.Plan).Length;
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(new ActionScope()).Length
            + JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, FieldValue> { ["payload"] = FieldValue.Of("padding") }).Length;
        var normalizedMetadata = document.Metadata with { CanonicalValueId = document.Metadata.ValueId };
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(new[] { normalizedMetadata }).Length;
        document.Metadata = document.Metadata with { Label = new string('x', maximum - planBytes - requestBytes - metadataBytes + excess) };
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();

        var preview = fixture.Session.PlanAction("plan", NoParameters);
        Assert.Equal(accepted, preview.PlanId is not null);
        Assert.Equal(accepted, preview.Plan.Errors.Count == 0);
    }

    [Fact]
    public void FailedReadOnlyValidationStillConsumesThePreview()
    {
        var document = new PlanningPlugin.PlanningDocument("save.dat");
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        var preview = fixture.Session.PlanAction("refill", NoParameters);
        document.ReadOnly = true;
        Assert.False(fixture.Session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        document.ReadOnly = false;
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId!, preview.Revision).Stale);
        Assert.Equal(0, document.ApplyCount);
    }

    private sealed class MetadataPlanner : BoundSaveDocument, IActionPlanningDocument, IFieldMetadataDocument
    {
        public MetadataPlanner() => RegisterField("value", IntegerSpec.UInt8, () => FieldValue.Of(1L), _ => { });
        public FieldMetadata Metadata { get; set; } = new("value", "", IntegerSpec.UInt8);
        public bool FailMetadata { get; init; }
        public ActionPlan Plan { get; } = new() { Changes = [new("value", "Value", FieldValue.Of(1L), FieldValue.Of(2L))] };
        public override string Title => "Metadata budget";
        public override ViewNode BuildView() => new ActionNode("plan", "Plan")
        {
            SupportsPlanning = true,
            Parameters = [new("payload", "Payload", new TextSpec(), FieldValue.Of("padding"))],
        };
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) =>
            FailMetadata ? throw new InvalidOperationException("Cannot resolve metadata.")
                : valueIds.Contains(Metadata.ValueId) ? [Metadata] : [];
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => Plan;
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) => new();
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
