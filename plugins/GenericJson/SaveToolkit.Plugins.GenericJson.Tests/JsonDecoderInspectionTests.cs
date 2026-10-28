using System.Text;
using System.Text.Json;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonDecoderInspectionTests
{
    [Fact]
    public void NativeHookExcludesUnrelatedProfileFamiliesAndRetainsExactStringsAndOccurrences()
    {
        const string source = """
            {"Profile":{"Unrelated":"not gameplay","Minigame.X":"true","Minigame.X":"false",
            "MinigameCounter.X_MayHighScore":"00017","LastSaveChapter":"/Game/Example##0"},"CloudSettings":{},"LocalSettings":{}}
            """;
        var file = MemorySaveSet.SingleFile("ue4savegame.dpx.sav", Encoding.UTF8.GetBytes(source), "PPSA02343");
        var decoder = new HookSaveDecoder(Assert.Single(new GenericJsonPlugin().Decoders));
        Assert.True(decoder.CanDecode(file));
        var tree = decoder.Decode(file, new());
        Assert.False(tree.Truncated);
        Assert.Equal(4, tree.Values.Count);
        Assert.DoesNotContain(tree.Values, v => v.Value == "not gameplay");
        Assert.Equal("true", tree.Values.Single(v => v.Path == "Profile/Minigame.X#1").Value);
        Assert.Equal("false", tree.Values.Single(v => v.Path == "Profile/Minigame.X#2").Value);
        Assert.Equal("00017", tree.Values.Single(v => v.Path.Contains("MayHighScore", StringComparison.Ordinal)).Value);
        Assert.All(tree.Values, v => Assert.Equal("string", v.Kind));
        Assert.True(decoder.Decode(file, new() { MaxValues = 1 }).Truncated);
        Assert.True(decoder.Decode(file, new() { MaxDepth = 0 }).Truncated);
        Assert.False(decoder.CanDecode(MemorySaveSet.SingleFile("data.json", "{}"u8.ToArray())));
    }

    [Theory]
    [InlineData("PPSA04027", "settings.json")]
    [InlineData("PPSA07696", "imgui_emu_option.json")]
    public void NativeHookReadsAllSampleGenerationsDeterministically(string title, string name)
    {
        var decoder = new HookSaveDecoder(Assert.Single(new GenericJsonPlugin().Decoders));
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f => f.FileName == name))
        {
            Assert.True(decoder.CanDecode(file));
            var first = decoder.Decode(file, new());
            Assert.False(first.Truncated);
            Assert.NotEmpty(first.Values);
            Assert.Equal(first.Values, decoder.Decode(file, new()).Values);
        }
    }

    [Fact]
    public void InspectionUsesCurrentEditedBytesAndPreservesRevisionDirtyStateAndHistory()
    {
        using var fixture = new JsonSessionFixture("[1]");
        var field = Assert.Single(fixture.Fields());
        Assert.True(fixture.Edit(field.ValueId, JsonEditingTests.Number("2.00000000000000000001")).Ok);
        var before = fixture.Bytes;
        var state = fixture.Session.State;
        var result = fixture.Session.InvokeAction("inspect-json", new Dictionary<string, JsonElement>(), state.Revision);
        Assert.True(result.Ok);
        Assert.Contains("Exact token round-trip", result.Message);
        Assert.Equal(state, fixture.Session.State);
        Assert.Equal(before, fixture.Bytes);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal("[1]", Encoding.UTF8.GetString(fixture.Bytes));
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(before, fixture.Bytes);
    }

    [Fact]
    public void PositionalWrapperArraysAreExplicitlyIncompleteEvidence()
    {
        var file = MemorySaveSet.SingleFile("imgui_emu_option.json", "{\"options\":{\"unknown\":[1,2]},\"settings\":{}}"u8.ToArray(), "PPSA07696");
        var decoder = new HookSaveDecoder(Assert.Single(new GenericJsonPlugin().Decoders));
        var tree = decoder.Decode(file, new());
        Assert.True(tree.Truncated);
        Assert.Equal(2, tree.Values.Count);
        Assert.Contains(tree.Warnings, w => w.Contains("positional", StringComparison.Ordinal));
    }
}
