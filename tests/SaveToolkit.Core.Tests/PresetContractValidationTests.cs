using System.Text.Json;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class PresetContractValidationTests
{
    [Fact]
    public void DirectionFlagsRejectCaptureAndApplyBeforeAdapterWork()
    {
        var captureOnly = Capability(new PresetActionCapability("set", "Set", [], false, true));
        var request = new PresetCaptureRequest("set", new Dictionary<string, JsonElement>());
        Assert.Contains("capture", PresetContractValidation.ValidateCapture(captureOnly, request), StringComparison.OrdinalIgnoreCase);

        var applyOnly = Capability(new PresetActionCapability("set", "Set", [], true, false));
        var action = new PresetAction("set", new Dictionary<string, JsonElement>());
        Assert.Contains("apply", PresetContractValidation.ValidateActions(applyOnly, [action]), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParameterBearingCapabilitiesRequireMatchingTypedSpecs()
    {
        var missing = Capability(new PresetActionCapability("set", "Set", ["quantity"]));
        Assert.Contains("metadata", PresetContractValidation.ValidateCapability(missing), StringComparison.OrdinalIgnoreCase);
        var valid = Capability(new PresetActionCapability("set", "Set", ["quantity"], ParameterSpecs: [new IntegerSpec(1, 99)]));
        var request = new PresetCaptureRequest("set", new Dictionary<string, JsonElement>
        {
            ["quantity"] = JsonDocument.Parse("0").RootElement.Clone(),
        });
        Assert.Contains("between", PresetContractValidation.ValidateCapture(valid, request), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescriptorListMayAdvertiseFiveActionsWhenOneActionIsPersisted()
    {
        var actions = Enumerable.Range(0, 5)
            .Select(index => new PresetActionCapability($"action-{index}", $"Action {index}", []))
            .ToArray();
        var capability = new PresetCapability("1", null, actions, MaxActions: 1);

        Assert.Null(PresetContractValidation.ValidateCapability(capability));
        Assert.Null(PresetContractValidation.ValidateActions(capability,
            [new PresetAction("action-0", new Dictionary<string, JsonElement>())]));
    }

    [Fact]
    public void CapabilityRejectsInvalidScopeAndOversizedDescriptorMetadata()
    {
        var invalidScope = new PresetCapability("1", null,
            [new PresetActionCapability("set", "Set", [], ScopeKinds: [ScopeKinds.Invalid])]);
        var oversizedLabel = new PresetCapability("1", null,
            [new PresetActionCapability("set", new string('x', PresetContractValidation.MaxMetadataLength + 1), [])]);

        Assert.Contains("malformed", PresetContractValidation.ValidateCapability(invalidScope), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("malformed", PresetContractValidation.ValidateCapability(oversizedLabel), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CapabilityAdmissionRejectsUnsafeFlagsAndRecursiveGraphs()
    {
        var invalidFlags = Capability(new PresetActionCapability("set", "Set", ["value"],
            ParameterSpecs: [new FlagsSpec([], -1)]));
        var oversizedFlags = Capability(new PresetActionCapability("set", "Set", ["value"],
            ParameterSpecs: [new FlagsSpec([], PresetContractValidation.MaxFlagWidth + 1)]));
        var badBits = Capability(new PresetActionCapability("set", "Set", ["value"],
            ParameterSpecs: [new FlagsSpec([new FlagBit(4, "Duplicate"), new FlagBit(4, "Duplicate")], 4)]));

        var options = new List<UnionOption>();
        var cycle = new UnionSpec(options);
        options.Add(new UnionOption("self", "Self", cycle));
        var deep = (FieldSpec)new BoolSpec();
        for (var index = 0; index < PresetContractValidation.MaxCapabilityDepth + 2; index++) deep = new NullableSpec(deep);
        var recursive = Capability(new PresetActionCapability("set", "Set", ["value"], ParameterSpecs: [cycle, deep]));

        Assert.False(PresetContractValidation.TryDetach(invalidFlags, out _, out _));
        Assert.False(PresetContractValidation.TryDetach(oversizedFlags, out _, out _));
        Assert.False(PresetContractValidation.TryDetach(badBits, out _, out _));
        Assert.False(PresetContractValidation.TryDetach(recursive, out _, out _));
    }

    [Fact]
    public void ResolutionBoundsRejectOversizedCandidateGraphsWithoutReturningThem()
    {
        var requested = new PresetInstanceReference("owned", "target");
        var candidates = Enumerable.Range(0, PresetContractValidation.MaxResolutionCandidates + 1)
            .Select(index => new PresetInstanceReference("owned", $"candidate-{index}"))
            .ToArray();
        var resolution = new PresetResolution([new PresetInstanceResolution(requested, PresetInstanceResolutionStatus.Ambiguous, candidates)]);

        var error = PresetContractValidation.ValidateResolution(resolution, out var serialized);

        Assert.NotNull(error);
        Assert.Null(serialized);
    }

    [Fact]
    public void ResolutionMustMatchRequestedTargetsAndChoices()
    {
        var requested = new PresetInstanceReference("owned", "target");
        var candidate = new PresetInstanceReference("owned", "candidate");
        var omitted = new PresetResolution([]);
        Assert.Contains("every requested", PresetContractValidation.ValidateResolution(omitted, [requested], null, false, out _), StringComparison.OrdinalIgnoreCase);

        var injected = new PresetResolution([
            new(requested, PresetInstanceResolutionStatus.Resolved),
            new(new PresetInstanceReference("owned", "extra"), PresetInstanceResolutionStatus.Resolved)]);
        Assert.Contains("exactly one result", PresetContractValidation.ValidateResolution(injected, [requested], null, false, out _), StringComparison.OrdinalIgnoreCase);

        var ambiguous = new PresetResolution([new(requested, PresetInstanceResolutionStatus.Ambiguous,
            [candidate, new PresetInstanceReference("owned", "candidate-2")])]);
        Assert.Contains("current ambiguous", PresetContractValidation.ValidateResolution(ambiguous, [requested],
            new Dictionary<string, string> { [requested.StableId] = "forged" }, false, out _), StringComparison.OrdinalIgnoreCase);
        var forged = new PresetResolution([new(requested, PresetInstanceResolutionStatus.Resolved,
            [candidate, new PresetInstanceReference("owned", "candidate-2")])]);
        Assert.Contains("exactly one", PresetContractValidation.ValidateResolution(forged, [requested], null, false, out _), StringComparison.OrdinalIgnoreCase);

        var unique = new PresetResolution([new(requested, PresetInstanceResolutionStatus.Resolved, [candidate])]);
        Assert.Null(PresetContractValidation.ValidateResolution(unique, [requested], null, false, out _));
    }

    private static PresetCapability Capability(PresetActionCapability action) =>
        new("1", null, [action], 1, "fixture", "sha256:fixture", "toolkit.action-parameters", "schema", "save");

    private static class ScopeKinds
    {
        public const string Invalid = "session";
    }
}
