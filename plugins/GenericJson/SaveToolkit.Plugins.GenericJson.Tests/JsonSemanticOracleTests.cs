using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonSemanticOracleTests
{
    private const string Cyberpunk = """
        {"version":136,"opaque":"\u0041","data":[{"group_name":"/test","options":[
        {"name":"Mode","type":"string_list","value":"Balanced","index":1,"default_index":2,"values":["Performance","Balanced","Quality"]},
        {"name":"Volume","type":"int","value":50,"min_value":0,"max_value":100,"step_value":5,"default_value":100},
        {"name":"Enabled","type":"bool","value":true,"default_value":false}]}]}
        """;
    private const string Profile = """
        {"Profile":{"Minigame.A":"true","MinigameFlag.A_HaveDiscovered":"True","MinigameFlag.B_HavePlayed":"TRUE",
        "MinigameCounter.A_CodyWins":"0012","FurthestUnlockedChapter":"/Imported/Unlocked",
        "LastSaveChapter":"/Imported/Chapter","LastSaveProgressPoint":"/Imported/Checkpoint","Unrelated":"\u0041"},
        "CloudSettings":{"unchanged":1.00},"LocalSettings":{}}
        """;
    private const string Wrapper = """
        {"options":{"gain":-0.5,"scale":5,"enabled":false},"settings":{"hint":"true"},"opaque":"\u0041"}
        """;

    [Theory]
    [InlineData("0", "Performance")]
    [InlineData("2", "Quality")]
    public void RecordedChoiceUsesRequestedEntryAndItsExactDerivedIndex(string index, string choice)
    {
        using var fixture = new JsonSessionFixture("\uFEFF" + Cyberpunk, title: "PPSA04027", fileName: "settings.json");
        // Choose the enum explicitly: the document has three raw fields named "value".
        var action = JsonSemanticTestHelper.Actions(fixture).Single(a => a.Parameters.FirstOrDefault()?.Spec is EnumSpec);
        Assert.IsType<EnumSpec>(Assert.Single(action.Parameters).Spec);
        var plan = JsonSemanticTestHelper.ApplyAndVerify(fixture, action.ActionId, ("value", FieldValue.Of(index)));
        AssertBytes(fixture, ("\uFEFF" + Cyberpunk).Replace("\"value\":\"Balanced\",\"index\":1", $"\"value\":\"{choice}\",\"index\":{index}", StringComparison.Ordinal));
        AssertChanges(plan,
            ("value/10", Text("Balanced"), Text(choice), false),
            ("value/11", Number("1"), Number(index), true));
    }

    [Fact]
    public void RecordedDefaultsUseFileDefaultsIncludingPairedChoiceAndBoolean()
    {
        using var fixture = new JsonSessionFixture(Cyberpunk, title: "PPSA04027", fileName: "settings.json");
        var plan = JsonSemanticTestHelper.ApplyAndVerify(fixture, "semantic/defaults");
        AssertBytes(fixture, Cyberpunk.Replace("\"value\":\"Balanced\",\"index\":1", "\"value\":\"Quality\",\"index\":2", StringComparison.Ordinal)
            .Replace("\"value\":50", "\"value\":100", StringComparison.Ordinal).Replace("\"value\":true", "\"value\":false", StringComparison.Ordinal));
        AssertChanges(plan,
            ("value/10", Text("Balanced"), Text("Quality"), false),
            ("value/11", Number("1"), Number("2"), true),
            ("value/20", Number("50"), Number("100"), false),
            ("value/28", Boolean(true), Boolean(false), false));
    }

    [Fact]
    public void ProgressTupleKeepsEachDistinctParameterOnItsNamedField()
    {
        using var fixture = OpenProfile();
        var plan = JsonSemanticTestHelper.ApplyAndVerify(fixture, "semantic/progress",
            ("FurthestUnlockedChapter", FieldValue.Of("/New/Unlocked")), ("LastSaveChapter", FieldValue.Of("/New/Chapter")),
            ("LastSaveProgressPoint", FieldValue.Of("/New/Checkpoint")));
        AssertBytes(fixture, Profile.Replace("/Imported/Unlocked", "/New/Unlocked", StringComparison.Ordinal)
            .Replace("/Imported/Chapter", "/New/Chapter", StringComparison.Ordinal).Replace("/Imported/Checkpoint", "/New/Checkpoint", StringComparison.Ordinal));
        AssertChanges(plan,
            ("value/6", Text("/Imported/Unlocked"), Text("/New/Unlocked"), false),
            ("value/7", Text("/Imported/Chapter"), Text("/New/Chapter"), false),
            ("value/8", Text("/Imported/Checkpoint"), Text("/New/Checkpoint"), false));
    }

    [Fact]
    public void PresentFlagPresetChangesEveryEligibleFlagWithItsOwnCasing()
    {
        using var fixture = OpenProfile();
        var plan = JsonSemanticTestHelper.ApplyAndVerify(fixture, "semantic/present-flags", ("enabled", FieldValue.Of(false)));
        AssertBytes(fixture, Profile.Replace("\"true\"", "\"false\"", StringComparison.Ordinal)
            .Replace("\"True\"", "\"False\"", StringComparison.Ordinal).Replace("\"TRUE\"", "\"FALSE\"", StringComparison.Ordinal));
        AssertChanges(plan,
            ("value/2", Text("true"), Text("false"), false),
            ("value/3", Text("True"), Text("False"), false),
            ("value/4", Text("TRUE"), Text("FALSE"), false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(67)]
    public void CounterBoundariesChangeTheRequestedStringAndZeroPresetRestoresZero(int value)
    {
        using var fixture = OpenProfile();
        var action = JsonSemanticTestHelper.SetFor(fixture, "MinigameCounter.A_CodyWins");
        var next = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var plan = JsonSemanticTestHelper.ApplyAndVerify(fixture, action.ActionId, ("value", FieldValue.Of(value)));
        AssertBytes(fixture, Profile.Replace("\"0012\"", $"\"{next}\"", StringComparison.Ordinal));
        AssertChanges(plan, ("value/5", Text("0012"), Text(next), false));
        if (value == 0) return;
        var reset = JsonSemanticTestHelper.ApplyAndVerify(fixture, "semantic/reset-counters");
        AssertBytes(fixture, Profile.Replace("\"0012\"", "\"0\"", StringComparison.Ordinal));
        AssertChanges(reset, ("value/5", Text("67"), Text("0"), false));
    }

    [Theory]
    [InlineData("gain", "-0.5", "-0.2500000000000000001", "value/2")]
    [InlineData("gain", "-0.5", "0", "value/2")]
    [InlineData("scale", "5", "4", "value/3")]
    [InlineData("scale", "5", "0", "value/3")]
    public void WrapperNumbersPersistRequestedExactLexemesAndImportedReset(string label, string original, string next, string id)
    {
        using var fixture = OpenWrapper();
        var action = JsonSemanticTestHelper.SetFor(fixture, label);
        var plan = JsonSemanticTestHelper.ApplyAndVerify(fixture, action.ActionId, ("value", FieldValue.ExactNumber(next)));
        AssertBytes(fixture, Wrapper.Replace($"\"{label}\":{original}", $"\"{label}\":{next}", StringComparison.Ordinal));
        AssertChanges(plan, (id, Number(original), Number(next), false));
        var reset = JsonSemanticTestHelper.ApplyAndVerify(fixture, "semantic/wrapper-imported");
        AssertBytes(fixture, Wrapper);
        AssertChanges(reset, (id, Number(next), Number(original), false));
    }

    [Fact]
    public void WrapperBooleanAndStringBooleanRetainTheirStorageTypes()
    {
        using var fixture = OpenWrapper();
        var enabled = JsonSemanticTestHelper.ApplyAndVerify(fixture, JsonSemanticTestHelper.SetFor(fixture, "enabled").ActionId, ("value", FieldValue.Of(true)));
        AssertBytes(fixture, Wrapper.Replace("\"enabled\":false", "\"enabled\":true", StringComparison.Ordinal));
        AssertChanges(enabled, ("value/4", Boolean(false), Boolean(true), false));
        var hint = JsonSemanticTestHelper.ApplyAndVerify(fixture, JsonSemanticTestHelper.SetFor(fixture, "hint").ActionId, ("value", FieldValue.Of(false)));
        AssertBytes(fixture, Wrapper.Replace("\"enabled\":false", "\"enabled\":true", StringComparison.Ordinal)
            .Replace("\"hint\":\"true\"", "\"hint\":\"false\"", StringComparison.Ordinal));
        AssertChanges(hint, ("value/6", Text("true"), Text("false"), false));
    }

    private static void AssertChanges(ActionPlan plan, params (string Id, FieldValue Old, FieldValue New, bool Derived)[] expected)
    {
        Assert.Empty(plan.Errors);
        Assert.Equal(expected.OrderBy(c => c.Id, StringComparer.Ordinal), plan.Changes
            .Select(c => (c.ValueId, c.OldValue, c.NewValue, c.Derived)).OrderBy(c => c.ValueId, StringComparer.Ordinal));
        Assert.All(plan.Changes, c => { Assert.True(c.OldExists); Assert.True(c.NewExists); });
    }

    private static void AssertBytes(JsonSessionFixture fixture, string expected) => Assert.Equal(Encoding.UTF8.GetBytes(expected), fixture.Bytes);
    private static FieldValue Text(string value) => JsonEditingTests.Text(value);
    private static FieldValue Number(string value) => JsonEditingTests.Number(value);
    private static FieldValue Boolean(bool value) => FieldValue.Union("boolean", FieldValue.Of(value));
    private static JsonSessionFixture OpenProfile() => new(Profile, title: "PPSA02343", fileName: "ue4savegame.dpx.sav");
    private static JsonSessionFixture OpenWrapper() => new(Wrapper, title: "PPSA07696", fileName: "imgui_emu_option.json");
}
