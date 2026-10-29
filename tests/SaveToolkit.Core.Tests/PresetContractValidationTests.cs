using System.Text.Json;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;

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
