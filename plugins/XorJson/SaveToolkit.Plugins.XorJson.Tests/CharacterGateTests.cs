using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class CharacterGateTests
{
    private const string TestedVersion = "\"SoftVersion\":2,\"FormatHash\":3777917472";

    public static TheoryData<string, string> ReadOnlyMetadata => new()
    {
        { TestedVersion, "" },
        { TestedVersion, "\"MetaData\":null," },
        { TestedVersion, "\"MetaData\":[]," },
        { TestedVersion, "\"MetaData\":{}," },
        { TestedVersion, "\"MetaData\":{\"IsOnline\":null}," },
        { TestedVersion, "\"MetaData\":{\"IsOnline\":\"false\"}," },
        { TestedVersion, "\"MetaData\":{\"IsOnline\":0}," },
        { TestedVersion, "\"MetaData\":{\"IsOnline\":true}," },
        { TestedVersion, "\"MetaData\":{\"IsOnline\":false,\"IsOnline\":false}," },
        { TestedVersion, "\"MetaData\":{\"IsOnline\":true,\"IsOnline\":false}," },
        { TestedVersion, "\"MetaData\":{\"IsOnline\":false},\"MetaData\":{\"IsOnline\":false}," },
        { "\"SoftVersion\":2", "\"MetaData\":{\"IsOnline\":false}," },
        { "\"FormatHash\":3777917472", "\"MetaData\":{\"IsOnline\":false}," },
        { TestedVersion + ",\"SoftVersion\":2", "\"MetaData\":{\"IsOnline\":false}," },
        { TestedVersion + ",\"FormatHash\":3777917472", "\"MetaData\":{\"IsOnline\":false}," },
        { "\"SoftVersion\":\"2\",\"FormatHash\":3777917472", "\"MetaData\":{\"IsOnline\":false}," },
        { "\"SoftVersion\":2,\"FormatHash\":null", "\"MetaData\":{\"IsOnline\":false}," },
    };

    [Theory]
    [MemberData(nameof(ReadOnlyMetadata))]
    public void MissingMalformedAndAmbiguousWriteGatesStayReadOnly(string version, string mode)
    {
        var text = "{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\"," + version + "}," +
            "\"CharacterSaveV1\":{" + mode + "\"Level\":1,\"future\":-0.00E+02}}";
        var original = Encoding.UTF8.GetBytes(text);
        var file = MemorySaveSet.SingleFile("hero.sav", original);
        var plugin = new XorJsonPlugin();
        Assert.True(plugin.Probe(file).IsMatch);
        using var document = plugin.Open(file);
        Assert.False(document.CanEdit);
        var fields = XorJsonTests.Fields(document);
        Assert.NotEmpty(fields);
        Assert.All(fields, field => Assert.True(field.Spec.ReadOnly));
        var level = fields.Single(field => field.Label == "Level");
        var edit = document.ApplyEdits([new(level.ValueId, FieldValue.ExactNumber("2"))]);
        Assert.False(edit.Succeeded);
        Assert.NotEmpty(edit.Errors);
        Assert.Empty(edit.Changes);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var plan = planner.PlanAction("json/bulk-set", new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of("2") }, new ActionScope([level.ValueId]));
        Assert.NotEmpty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Throws<SaveFormatException>(() => document.InvokeAction("json/remove/0", new Dictionary<string, FieldValue>()));
        Assert.Equal(level.Value, document.GetValue(level.ValueId));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData("{\"HardFormat\":\"FCharacterSaveV1\",\"HardFormat\":\"FCharacterSaveV1\"}", "{}")]
    [InlineData("null", "{}")]
    [InlineData("[]", "{}")]
    [InlineData("{\"HardFormat\":\"FCharacterSaveV1\"}", "null")]
    [InlineData("{\"HardFormat\":\"FCharacterSaveV1\"}", "[]")]
    public void MalformedOrAmbiguousPlainCharacterIdentityIsDeclined(string meta, string hero)
    {
        var text = "{\"SerializeMeta\":" + meta + ",\"CharacterSaveV1\":" + hero + "}";
        var file = MemorySaveSet.SingleFile("hero.sav", Encoding.UTF8.GetBytes(text));
        var plugin = new XorJsonPlugin();
        Assert.False(plugin.Probe(file).IsMatch);
        Assert.Throws<SaveFormatException>(() => plugin.Open(file));
    }

    [Theory]
    [InlineData("SoftVersion", "3")]
    [InlineData("FormatHash", "1")]
    public void RejectedCharacterGateEditRestoresSdkHistoryBeforeStructuralUndo(string gateLabel, string replacement)
    {
        const string text = "{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\",\"SoftVersion\":2,\"FormatHash\":3777917472}," +
            "\"CharacterSaveV1\":{\"MetaData\":{\"IsOnline\":false},\"Level\":1}}";
        var original = XorJsonTests.Encode(text);
        using var document = XorJsonTests.Open(original);
        var fields = XorJsonTests.Fields(document);
        var gate = fields.Single(field => field.Label == gateLabel);
        var level = fields.Single(field => field.Label == "Level");
        var typed = Assert.IsType<JsonSaveDocument>(document);
        var before = typed.CaptureDocumentMemento();

        var rejected = document.ApplyEdits([new(gate.ValueId, FieldValue.ExactNumber(replacement))]);

        Assert.False(rejected.Succeeded);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.Equal(gate.Value, document.GetValue(gate.ValueId));
        var afterFailure = typed.CaptureDocumentMemento();
        var afterState = Assert.IsType<SnapshotMemento<JsonModelSnapshot>>(afterFailure).State;
        Assert.Empty(afterState.LosslessReplacements);
        Assert.Equal(before.SizeBytes, afterFailure.SizeBytes);

        Assert.True(document.ApplyEdits([new(level.ValueId, FieldValue.ExactNumber("2"))]).Succeeded);
        var changed = document.Serialize().Single().Content.ToArray();
        var structural = document.InvokeAction("json/add/0", new Dictionary<string, FieldValue>
        {
            ["key"] = FieldValue.Of("r3"),
            ["json"] = FieldValue.Of("1"),
        });
        var structuralBytes = document.Serialize().Single().Content.ToArray();
        Assert.NotEqual(Convert.ToHexString(changed), Convert.ToHexString(structuralBytes));

        var redo = document.RestoreMemento(structural.Revert!);
        Assert.Equal(changed, document.Serialize().Single().Content.ToArray());
        _ = document.RestoreMemento(redo);
        Assert.Equal(structuralBytes, document.Serialize().Single().Content.ToArray());

        _ = document.RestoreMemento(afterFailure);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }
}
