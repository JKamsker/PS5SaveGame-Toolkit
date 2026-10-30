using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class PresetParameterLimitConsistencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredLongTypedParameterSurvivesTransferAndReopen(bool exactNumber)
    {
        var template = PresetTransferFixture.Preset();
        var value = exactNumber
            ? JsonSerializer.SerializeToElement(FieldValue.ExactNumber(new string('7', ExactJsonNumber.HardMaxLength)))
            : JsonSerializer.SerializeToElement(new string('t', 4097));
        var store = PresetTransferFixture.Store();
        var saved = store.Create(template.Name, template.Compatibility,
            [template.Actions[0] with { Parameters = new Dictionary<string, JsonElement> { ["value"] = value } }]);

        PresetMetadataLimitConsistencyTests.AssertTransferAndReopen(store, saved);
    }
}
