using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class PresetDepthLimitConsistencyTests
{
    [Theory]
    [InlineData(17)]
    [InlineData(59)]
    public void StoredNestedUnionParameterSurvivesTransferAndReopen(int nesting)
    {
        var template = PresetTransferFixture.Preset();
        var value = FieldValue.Of("leaf");
        for (var depth = 0; depth < nesting; depth++) value = FieldValue.Union("variant", value);
        var store = PresetTransferFixture.Store();
        var saved = store.Create(template.Name, template.Compatibility,
            [template.Actions[0] with { Parameters = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(value) } }]);

        PresetMetadataLimitConsistencyTests.AssertTransferAndReopen(store, saved);
    }
}
