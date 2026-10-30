using System.Text.Json;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class PresetMetadataLimitConsistencyTests
{
    [Theory]
    [InlineData("pluginId")]
    [InlineData("pluginVersion")]
    [InlineData("presetKind")]
    [InlineData("titleCount")]
    [InlineData("titleLength")]
    [InlineData("instanceKind")]
    [InlineData("instanceLabel")]
    [InlineData("instanceRole")]
    [InlineData("instanceBuild")]
    public void StoredMetadataAtItsAcceptedLimitSurvivesTransferAndReopen(string field)
    {
        var template = PresetTransferFixture.Preset();
        var key = new string('k', PresetContractValidation.MaxKeyLength);
        var metadata = new string('m', PresetContractValidation.MaxMetadataLength);
        var compatibility = field switch
        {
            "pluginId" => template.Compatibility with { PluginId = key },
            "pluginVersion" => template.Compatibility with { PluginVersion = metadata },
            "presetKind" => template.Compatibility with { PresetKind = metadata },
            "titleCount" => template.Compatibility with
            {
                TitleIds = Enumerable.Range(0, PresetContractValidation.MaxMapEntries).Select(i => $"PPSA{i:D5}").ToArray(),
            },
            "titleLength" => template.Compatibility with { TitleIds = [key] },
            _ => template.Compatibility,
        };
        var instance = field switch
        {
            "instanceKind" => template.Actions[0].Instances[0] with { Kind = key },
            "instanceLabel" => template.Actions[0].Instances[0] with { Label = metadata },
            "instanceRole" => template.Actions[0].Instances[0] with { Role = metadata },
            "instanceBuild" => template.Actions[0].Instances[0] with { Build = metadata },
            _ => template.Actions[0].Instances[0],
        };
        var store = PresetTransferFixture.Store();
        var saved = store.Create(template.Name, compatibility, [template.Actions[0] with { Instances = [instance] }]);
        AssertTransferAndReopen(store, saved);
    }

    internal static void AssertTransferAndReopen(PresetStore source, ToolkitPreset saved)
    {
        var bytes = new PresetTransferService(source).Export(new([saved.Id]));
        var root = TestZips.TempDir();
        var destination = PresetTransferFixture.Store(root);
        var transfer = new PresetTransferService(destination);
        var preview = PresetTransferFixture.Resolve(transfer, transfer.Preview(bytes));
        transfer.Commit(new(preview.Token));
        var reopened = PresetTransferFixture.Store(root).Find(saved.Id);
        Assert.NotNull(reopened);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(saved), JsonSerializer.SerializeToElement(reopened)));
    }
}
