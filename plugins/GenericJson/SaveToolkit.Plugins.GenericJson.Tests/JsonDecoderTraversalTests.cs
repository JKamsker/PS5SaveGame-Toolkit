using System.Text;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonDecoderTraversalTests
{
    private const string Cyberpunk = """
        {"version":136,"data":[{"group_name":"/x~","options":[
        {"name":"Mode","value":-0,"index":0,"default_value":1e+00,"default_index":1,"ignored":true},
        {"name":"Mode","value":2},{"name":"bad","name":"bad","value":9}]},
        {"group_name":"/x~","options":[{"name":"Mode","value":false}]}]}
        """;
    private const string Wrapper = "{\"options\":{\"x\":[null,{\"a\":true}],\"x\":1e999999999999999999999},\"settings\":{\"s\":\"é\"}}";
    private const string Profile = "{\"Profile\":{\"Minigame.A\":\"True\",\"Minigame.A\":\"false\",\"LastSaveChapter\":\"raw\",\"private\":\"omit\"},\"CloudSettings\":{},\"LocalSettings\":{}}";
    private static readonly string[] DepthLimitedValues = ["options#1/x#2|number|1e999999999999999999999", "settings#1/s#1|string|é"];

    public static TheoryData<string, string, string, string[]> Cases => new()
    {
        { "PPSA04027", "settings.json", Cyberpunk, [
            "settings/~1x~0#1/Mode#1/value#1|number|-0", "settings/~1x~0#1/Mode#1/index#1|number|0",
            "settings/~1x~0#1/Mode#1/default_value#1|number|1e+00", "settings/~1x~0#1/Mode#1/default_index#1|number|1",
            "settings/~1x~0#1/Mode#2/value#1|number|2", "settings/~1x~0#2/Mode#1/value#1|boolean|false"] },
        { "PPSA07696", "imgui_emu_option.json", Wrapper, [
            "options#1/x#1/0|null|null", "options#1/x#1/1/a#1|boolean|true",
            "options#1/x#2|number|1e999999999999999999999", "settings#1/s#1|string|é"] },
        { "PPSA02343", "ue4savegame.dpx.sav", Profile, [
            "Profile/Minigame.A#1|string|True", "Profile/Minigame.A#2|string|false", "Profile/LastSaveChapter#1|string|raw"] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void OrderedTitleEvidenceStopsAtEveryRefusedValue(string title, string name, string source, string[] expected)
    {
        var file = MemorySaveSet.SingleFile(name, Encoding.UTF8.GetBytes(source), title);
        var decoder = Assert.Single(new GenericJsonPlugin().Decoders);
        var complete = new RecordingSink();
        decoder.Decode(file, complete);
        Assert.Equal(expected, complete.Values);
        for (var refused = 1; refused <= expected.Length; refused++)
        {
            var sink = new RecordingSink(refused);
            decoder.Decode(file, sink);
            Assert.Equal(expected.Take(refused), sink.Values);
            Assert.Equal(complete.Events.Take(sink.Events.Count), sink.Events);
        }
    }

    [Fact]
    public void DepthLimitMarksEachOmittedBranchAndContinuesWithLaterSiblings()
    {
        var file = MemorySaveSet.SingleFile("imgui_emu_option.json", Encoding.UTF8.GetBytes(Wrapper), "PPSA07696");
        var sink = new RecordingSink { MaxDepth = 1 };
        Assert.Single(new GenericJsonPlugin().Decoders).Decode(file, sink);
        Assert.Equal(DepthLimitedValues, sink.Values);
        Assert.Equal(2, sink.Events.Count(e => e == "truncated:JSON title traversal reached the decoder depth limit."));
    }

    private sealed class RecordingSink(int refuseAt = int.MaxValue) : ISaveDecodeSink
    {
        public int MaxValues => int.MaxValue;
        public int MaxValueLength => int.MaxValue;
        public int MaxDepth { get; init; } = 64;
        public long MaxFileBytes => long.MaxValue;
        public List<string> Values { get; } = [];
        public List<string> Events { get; } = [];
        public bool Add(string path, string kind, string value, string? label = null)
        {
            var entry = $"{path}|{kind}|{value}";
            Values.Add(entry);
            Events.Add("value:" + entry);
            return Values.Count < refuseAt;
        }
        public void Warn(string message) => Events.Add("warning:" + message);
        public void MarkTruncated(string reason) => Events.Add("truncated:" + reason);
    }
}
