using System.Text.Json;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectWireSpecTests
{
    private const int MaximumBytes = ActionPlanSnapshots.MaxSerializedBytes;

    [Fact]
    public void ActualPreviewAcceptsLargeNestedEnumLabelByWireBytesWithoutCaptureOrWriter()
    {
        using var document = new WireSpecDocument { OptionLabel = new string('x', 2_100_000) };
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();
        var planBytes = WireBytes(document.Plan());
        Assert.InRange(planBytes, 2_100_000, 2_102_000);

        var preview = fixture.Session.PlanAction("plan", new Dictionary<string, JsonElement>());

        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Errors);
        var row = Assert.Single(preview.Plan.Changes);
        Assert.Equal(FieldValue.Of("a"), row.OldValue);
        Assert.Equal(FieldValue.Of("b"), row.NewValue);
        Assert.True(row.OldExists && row.NewExists);
        var spec = Assert.IsType<EnumSpec>(row.Effect!.Spec);
        Assert.True(spec.ReadOnly);
        Assert.Equal(document.OptionLabel, spec.Options[0].Label);
        Assert.Equal(0, document.CaptureCalls);
        Assert.Equal(0, document.WriterCalls);
    }

    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, false)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    public void ActualNestedSpecSharesExactInclusiveWireAllowanceWithRequestAndCanonicalClosure(
        bool canonical, int excess, bool accepted)
    {
        using var document = new WireSpecDocument { IncludeCanonical = canonical };
        var requestBytes = WireBytes(new Dictionary<string, FieldValue>()) + WireBytes(new ActionScope());
        var metadataBytes = WireBytes(document.Descriptors());
        var fixedBytes = requestBytes + metadataBytes + WireBytes(document.Plan());
        document.OptionLabel = new string('x', MaximumBytes - fixedBytes + excess);
        // Independent production serializer bytes include every unplanned canonical dependency.
        Assert.Equal(MaximumBytes + excess, requestBytes + metadataBytes + WireBytes(document.Plan()));
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();
        var before = fixture.Session.State;
        var initialResolves = document.ResolverCalls;

        var preview = fixture.Session.PlanAction("plan", new Dictionary<string, JsonElement>());

        Assert.Equal(accepted, preview.PlanId is not null);
        Assert.Equal(before, fixture.Session.State);
        Assert.Equal(0, document.CaptureCalls);
        Assert.Equal(0, document.WriterCalls);
        Assert.Equal(accepted && canonical ? 3 : 0, document.GetterCalls);
        // +1 with no descriptors cannot even admit the empty closure, so no authority callback.
        Assert.Equal(canonical ? 2 : accepted ? 1 : 0, document.ResolverCalls - initialResolves);
        if (accepted)
        {
            var row = preview.Plan.Changes[0];
            Assert.Equal(FieldValue.Of("a"), row.OldValue);
            Assert.Equal(FieldValue.Of("b"), row.NewValue);
            Assert.Equal(document.OptionLabel, Assert.IsType<EnumSpec>(row.Effect!.Spec).Options[0].Label);
        }
        else Assert.Contains(preview.Plan.Errors, error => error.Message.Contains("large", StringComparison.Ordinal));
    }

    private static int WireBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, MetadataJson.Options).Length;

    private sealed class WireSpecDocument : BoundSaveDocument, IActionEffectDocument
    {
        public string OptionLabel { get; set; } = "";
        public bool IncludeCanonical { get; init; }
        public int ResolverCalls { get; private set; }
        public int GetterCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int WriterCalls { get; private set; }
        public override string Title => "Effect wire spec";
        public WireSpecDocument()
        {
            RegisterField("alias", IntegerSpec.Int32 with { ReadOnly = true }, GetCanonical);
            RegisterField("primary", IntegerSpec.Int32, GetCanonical);
        }
        private FieldValue GetCanonical() { GetterCalls++; return FieldValue.Of(1); }
        public IReadOnlyList<FieldMetadata> Descriptors() => IncludeCanonical
            ? [new("alias", "Alias", IntegerSpec.Int32 with { ReadOnly = true })
                { BindingKind = FieldBindingKind.Derived, CanonicalValueId = "primary" },
                new("primary", "Primary", IntegerSpec.Int32) { CanonicalValueId = "primary" }]
            : [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids)
        {
            ResolverCalls++;
            return Descriptors().Where(field => ids.Contains(field.ValueId, StringComparer.Ordinal)).ToArray();
        }
        public override ViewNode BuildView() => new ActionNode("plan", "Plan") { SupportsPlanning = true };
        public ActionPlan Plan()
        {
            var effect = new PlannedChange("effect", "Effect", FieldValue.Of("a"), FieldValue.Of("b"))
            {
                Effect = new(ActionEffectKind.Observation,
                    new EnumSpec([new("a", OptionLabel), new("b", "B")]) { ReadOnly = true }),
            };
            return new ActionPlan { Changes = IncludeCanonical
                ? [effect, new("alias", "Alias", FieldValue.Of(1), FieldValue.Of(2)) { Derived = true }]
                : [effect] };
        }
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => Plan();
        public DocumentMemento CaptureMemento() { CaptureCalls++; return DocumentMemento.Snapshot(new byte[] { 1 }, 1); }
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
        { WriterCalls++; throw new InvalidOperationException("Preview must not invoke the writer."); }
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { 1 })];
    }
}
