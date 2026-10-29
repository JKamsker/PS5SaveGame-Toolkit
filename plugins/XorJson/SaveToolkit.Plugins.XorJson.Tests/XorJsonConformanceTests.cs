using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class XorJsonConformanceTests : PluginConformanceTests<XorJsonPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA16064"];
    protected override int MaxFilesPerTitle => int.MaxValue;

    [Theory]
    [InlineData(false, "1")]
    [InlineData(false, "2")]
    [InlineData(true, "49")]
    [InlineData(true, "50")]
    public void ConformanceChoosesARealEditAndPreservesUnrelatedTokens(bool settings, string originalValue)
    {
        var text = settings ? "{\"masterVolume\":" + originalValue + ",\"future\":-0.00E+02}"
            : "{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\",\"SoftVersion\":2,\"FormatHash\":3777917472}," +
                "\"CharacterSaveV1\":{\"MetaData\":{\"IsOnline\":false,\"Level\":" + originalValue + "},\"future\":-0.00E+02}}";
        var bytes = settings ? XorJsonTests.Encode(text) : System.Text.Encoding.UTF8.GetBytes(text);
        using var document = XorJsonTests.Open(bytes);
        var fields = XorJsonTests.Fields(document);
        var edit = Assert.Single(ChooseEdits(document, fields));
        var field = fields.Single(candidate => candidate.ValueId == edit.ValueId);
        Assert.NotEqual(field.Value, edit.Value);
        var outcome = document.ApplyEdits([edit]);
        Assert.True(outcome.Succeeded);
        var change = Assert.Single(outcome.Changes);
        Assert.Equal(field.Value, change.OldValue);
        Assert.Equal(edit.Value, change.NewValue);
        var property = settings ? "masterVolume" : "Level";
        var expected = text.Replace("\"" + property + "\":" + originalValue,
            "\"" + property + "\":" + edit.Value.AsExactNumber(), StringComparison.Ordinal);
        Assert.Equal(settings ? XorJsonTests.Encode(expected) : System.Text.Encoding.UTF8.GetBytes(expected),
            document.Serialize().Single().Content.ToArray());
    }

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        if (!document.CanEdit) return [];
        var volume = fields.SingleOrDefault(f => f.Label == "masterVolume");
        if (volume is not null)
            return [new(volume.ValueId, FieldValue.ExactNumber(volume.Value.AsExactNumber() == "49" ? "50" : "49"))];
        var level = fields.Single(f => f.Label == "Level" && f.Description!.Contains("MetaData", StringComparison.Ordinal));
        return [new(level.ValueId, FieldValue.ExactNumber(level.Value.AsExactNumber() == "2" ? "1" : "2"))];
    }
}
