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

    private static PresetCapability Capability(PresetActionCapability action) =>
        new("1", null, [action], 1, "fixture", "sha256:fixture", "toolkit.action-parameters", "schema", "save");
}
