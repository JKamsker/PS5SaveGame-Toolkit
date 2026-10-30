using System.Text.Json;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class PresetDocumentTests
{
    [Fact]
    public void AmbiguousResolutionBlocksPlanWithoutMutation()
    {
        using var fixture = new MutationSessionFixture(new PresetFixtureDocument { Resolution = PresetInstanceResolutionStatus.Ambiguous });
        _ = fixture.Session.BuildView();
        var preset = Preset("owned-weapon");
        var preview = fixture.Session.PlanPreset(preset);
        Assert.Null(preview.PlanId);
        Assert.Equal(PresetInstanceResolutionStatus.Ambiguous, preview.Resolution.Instances.Single().Status);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void MissingAndUnsupportedResolutionAreNonChoosableAndBlockWholeApply()
    {
        foreach (var status in new[] { PresetInstanceResolutionStatus.Missing, PresetInstanceResolutionStatus.Unsupported })
        {
            using var fixture = new MutationSessionFixture(new PresetFixtureDocument { Resolution = status });
            _ = fixture.Session.BuildView();
            var preview = fixture.Session.PlanPreset(Preset("unresolved"));
            Assert.Null(preview.PlanId);
            Assert.Equal(status, preview.Resolution.Instances.Single().Status);
            Assert.Contains(status.ToString().ToLowerInvariant(), preview.Plan.Errors.Single().Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(fixture.Session.State.CanUndo);
        }
    }

    [Fact]
    public void AdapterApplyIsAtomicAndOneUndoStep()
    {
        var document = new PresetFixtureDocument();
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();
        var preview = fixture.Session.PlanPreset(Preset("value"));
        Assert.True(preview.PlanId is not null, string.Join(" | ", preview.Plan.Errors.Select(error => error.Message)));
        Assert.Equal(0, document.Value);
        Assert.True(fixture.Session.ApplyPresetPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(9, document.Value);
        Assert.True(fixture.Session.State.CanUndo);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(0, document.Value);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void ExplicitChoiceIsPassedToResolutionInsteadOfGuessing()
    {
        var document = new PresetFixtureDocument { Resolution = PresetInstanceResolutionStatus.Ambiguous };
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();
        var preview = fixture.Session.PlanPreset(Preset("value"), choices: new Dictionary<string, string> { ["id"] = "candidate" });
        Assert.True(preview.PlanId is not null, string.Join(" | ", preview.Plan.Errors.Select(error => error.Message)));
        Assert.Equal(PresetInstanceResolutionStatus.Resolved, preview.Resolution.Instances.Single().Status);
    }

    [Fact]
    public void CoreCaptureDoesNotPassSelectionKeysToNonSelectedScope()
    {
        var document = new PresetFixtureDocument();
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();

        _ = fixture.Session.CapturePreset(new PresetCaptureRequest("set",
            new Dictionary<string, JsonElement>(), PresetScopeKinds.Document, ["physical-row"]));

        Assert.Null(document.CapturedSelection);
    }

    [Fact]
    public void InvalidPublishedCapabilityIsDetachedBeforeDocumentPublication()
    {
        var document = new PresetFixtureDocument
        {
            CapabilityOverride = new PresetCapability("1", "r19", [new PresetActionCapability("set", "Set", ["value"],
                ParameterSpecs: [new FlagsSpec([], -1)])]),
        };
        using var fixture = new MutationSessionFixture(document);

        var dto = SaveToolkit.Api.Contracts.DtoMapper.ToDto(fixture.Session);

        Assert.Null(dto.PresetCapability);
        Assert.Equal("malformed", dto.PresetCapabilityStatus);
        Assert.Contains("malformed", dto.PresetCapabilityReason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(fixture.Session.PresetDocument);
        Assert.Equal(0, document.ResolveCalls);
    }

    [Fact]
    public void AggregateInstanceBoundRejectsBeforeAdapterResolution()
    {
        var document = new PresetFixtureDocument();
        using var fixture = new MutationSessionFixture(document);
        var references = Enumerable.Range(0, 128).Select(index => new PresetInstanceReference("owned", $"a-{index}")).ToArray();
        var extra = new PresetInstanceReference("owned", "b-extra");
        var preset = Preset("aggregate") with
        {
            Actions = [new PresetAction("set", new Dictionary<string, JsonElement>(), references),
                new PresetAction("set", new Dictionary<string, JsonElement>(), [.. references.Select(reference => reference with { StableId = $"b-{reference.StableId}" }), extra])],
        };

        var preview = fixture.Session.PlanPreset(preset);

        Assert.Null(preview.PlanId);
        Assert.Contains("aggregate", preview.Plan.Errors.Single().Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, document.ResolveCalls);
    }

    [Fact]
    public void AdapterResolutionFailureBecomesRecoverablePreviewError()
    {
        var document = new PresetFixtureDocument { ThrowOnResolve = true };
        using var fixture = new MutationSessionFixture(document);
        var preview = fixture.Session.PlanPreset(Preset("resolve-failure"));

        Assert.Null(preview.PlanId);
        Assert.Contains("planning failed", preview.Plan.Errors.Single().Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Theory]
    [InlineData(ResolutionShape.Omitted)]
    [InlineData(ResolutionShape.Injected)]
    [InlineData(ResolutionShape.Duplicate)]
    [InlineData(ResolutionShape.ForgedResolved)]
    public void MalformedResolutionCannotCreateAPlan(ResolutionShape shape)
    {
        var document = new PresetFixtureDocument { Shape = shape };
        using var fixture = new MutationSessionFixture(document);
        var preview = fixture.Session.PlanPreset(Preset("malformed"));
        Assert.Null(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void RejectedSecondResolutionPublishesOnlyLastValidatedGraph()
    {
        var document = new PresetFixtureDocument { Resolution = PresetInstanceResolutionStatus.Ambiguous, Shape = ResolutionShape.SecondOversized };
        using var fixture = new MutationSessionFixture(document);
        var preview = fixture.Session.PlanPreset(Preset("second"), choices: new Dictionary<string, string> { ["id"] = "candidate" });
        Assert.Null(preview.PlanId);
        Assert.Single(preview.Resolution.Instances);
        Assert.Equal(PresetInstanceResolutionStatus.Ambiguous, preview.Resolution.Instances[0].Status);
        Assert.Equal(2, preview.Resolution.Instances[0].Candidates.Count);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void OptInCapabilityRejectsMissingPublishedCompatibilityBoundaries()
    {
        using var fixture = new MutationSessionFixture(new PresetFixtureDocument());
        var missing = Preset("value") with
        {
            Compatibility = new PresetCompatibility("test.counter", "1.0.0", ["UNKNOWN"], "r19", null, null,
                true, "toolkit.action-parameters", null, null, "1"),
        };
        var result = PresetStore.CheckCompatibility(missing, fixture.Session);
        Assert.False(result.Compatible);
        Assert.Equal("schemaMismatch", result.Code);
        Assert.Contains("variant", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptInCapabilityAcceptsCompleteKnownBoundariesAndUnknownBuildStaysUnknown()
    {
        using var fixture = new MutationSessionFixture(new PresetFixtureDocument());
        var complete = Preset("value") with
        {
            Compatibility = new PresetCompatibility("test.counter", "1.0.0", ["UNKNOWN"], "r19", "fixture", null,
                true, "toolkit.action-parameters", null, null, "1"),
        };
        var result = PresetStore.CheckCompatibility(complete, fixture.Session);
        Assert.True(result.Compatible, result.Message);
        var unknownBuild = complete with { Compatibility = complete.Compatibility with { GameBuild = "r20" } };
        var mismatch = PresetStore.CheckCompatibility(unknownBuild, fixture.Session);
        Assert.False(mismatch.Compatible);
        Assert.Equal("buildMismatch", mismatch.Code);
        using var unknownFixture = new MutationSessionFixture(new PresetFixtureDocument { GameBuild = null });
        var unknown = PresetStore.CheckCompatibility(complete, unknownFixture.Session);
        Assert.False(unknown.Compatible);
        Assert.Contains("unknown", unknown.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ToolkitPreset Preset(string id) => new("1", id, id,
        new PresetCompatibility("test", "1", ["PPSA99999"]),
        [new PresetAction("set", new Dictionary<string, JsonElement>(), [new("owned", "id")])], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public enum ResolutionShape { Normal, Omitted, Injected, Duplicate, ForgedResolved, SecondOversized }

    private sealed class PresetFixtureDocument : BoundSaveDocument, IPresetDocument
    {
        public int Value { get; private set; }
        public int ResolveCalls { get; private set; }
        public IReadOnlyList<string>? CapturedSelection { get; private set; }
        public bool ThrowOnResolve { get; init; }
        public PresetCapability? CapabilityOverride { get; init; }
        public ResolutionShape Shape { get; init; }
        public PresetInstanceResolutionStatus Resolution { get; init; } = PresetInstanceResolutionStatus.Resolved;
        public string? GameBuild { get; init; } = "r19";
        public override string Title => "Preset fixture";
        public PresetCapability PresetCapability => CapabilityOverride ?? new("1", GameBuild, [new("set", "Set", [])], 4, "fixture", "pack");
        public PresetAction CapturePreset(PresetCaptureRequest request)
        {
            CapturedSelection = request.SelectedRowKeys;
            return new(request.ActionId, request.Parameters, [], request.ScopeKind);
        }
        public override ViewNode BuildView() => new SectionNode("Fixture", [Field("value", "Value", IntegerSpec.Int32,
            () => FieldValue.Of((long)Value), v => Value = v.AsInt32())]);
        public PresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances) =>
            Resolve(instances);
        public PresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances, IReadOnlyDictionary<string, string> choices) =>
            Shape == ResolutionShape.SecondOversized && choices.Count > 0
                ? new([new PresetInstanceResolution(instances[0], PresetInstanceResolutionStatus.Ambiguous,
                    Enumerable.Range(0, PresetContractValidation.MaxResolutionCandidates + 1)
                        .Select(index => new PresetInstanceReference("owned", $"candidate-{index}")).ToArray())])
            : choices.Count > 0
                ? new(instances.Select(instance => new PresetInstanceResolution(instance, PresetInstanceResolutionStatus.Resolved,
                    [new("owned", choices[instance.StableId])])).ToArray())
                : ResolvePresetInstances(instances);
        private PresetResolution Resolve(IReadOnlyList<PresetInstanceReference> instances)
        {
            ResolveCalls++;
            if (ThrowOnResolve) throw new InvalidOperationException("synthetic resolver failure");
            var results = instances.Select(instance => new PresetInstanceResolution(instance, Resolution,
                Resolution == PresetInstanceResolutionStatus.Ambiguous
                    ? [new PresetInstanceReference("owned", "candidate"), new PresetInstanceReference("owned", "candidate-2")]
                    : Resolution == PresetInstanceResolutionStatus.Resolved
                        ? [new PresetInstanceReference("owned", "candidate")]
                        : [])).ToList();
            return Shape switch
            {
                ResolutionShape.Omitted => new(results.Skip(1).ToArray()),
                ResolutionShape.Injected => new([.. results, new(new("owned", "extra"), PresetInstanceResolutionStatus.Resolved)]),
                ResolutionShape.Duplicate => new([.. results, results[0]]),
                ResolutionShape.ForgedResolved => new([new(results[0].Requested, PresetInstanceResolutionStatus.Resolved,
                    [new PresetInstanceReference("owned", "forged"), new PresetInstanceReference("owned", "forged-2")])]),
                _ => new(results),
            };
        }
        public ActionPlan PlanPreset(IReadOnlyList<PresetAction> actions, PresetResolution resolution) =>
            new() { Changes = [new PlannedChange("value", "Value", FieldValue.Of((long)Value), FieldValue.Of(9L))] };
        public EditOutcome ApplyPreset(IReadOnlyList<PresetAction> actions, PresetResolution resolution, ActionPlan plan) =>
            ApplyEdits([new ValueEdit("value", FieldValue.Of(9L))]);
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
