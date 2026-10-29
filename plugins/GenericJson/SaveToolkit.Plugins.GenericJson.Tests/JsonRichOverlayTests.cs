using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonRichOverlayTests
{
    [Fact]
    public void CyberpunkShowsFriendlyNamesRecordedBoundsAndPlannedAdaptersWithoutAliasBindings()
    {
        const string source = """
            {"version":136,"data":[{"group_name":"/audio/volume","options":[
              {"name":"MasterVolume","type":"int","value":50,"min_value":0,"max_value":100,"step_value":5,"default_value":100},
              {"name":"FrameRateModes","type":"string_list","value":"Quality","index":1,"values":["Performance","Quality"]},
              {"name":"UnknownDynamicChoice","type":"string_list","value":"raw","index":0,"is_dynamic":true}
            ]}]}
            """;
        using var document = Open("PPSA04027", "settings.json", source);
        var nodes = OverlayNodes(document);
        Assert.Contains(nodes.OfType<SectionNode>(), n => n.Title == "Master Volume");
        Assert.Contains(nodes.OfType<SectionNode>(), n => n.Title == "Recorded Min value");
        Assert.Contains(nodes.OfType<TextNode>(), n => n.Text.Contains("exactly matches the recorded choice", StringComparison.Ordinal));
        Assert.Contains(nodes.OfType<TextNode>(), n => n.Text.Contains("No recorded choice list", StringComparison.Ordinal));
        Assert.Contains(nodes.OfType<SectionNode>(), n => n.Description?.Contains("MasterVolume", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(nodes, n => n is FieldNode);
        Assert.All(nodes.OfType<ActionNode>(), action => Assert.True(action.SupportsPlanning));
        Assert.Contains(nodes.OfType<ActionNode>(), action => action.ActionId == "semantic/defaults");
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void ProfileGroupsPresentFlagCounterAndMinigameKeysWithoutNormalizingStringCasing()
    {
        const string source = """
            {"Profile":{"Minigame.Plunger":"true","MinigameFlag.Plunger_HaveDiscovered":"True",
              "MinigameCounter.Plunger_CodyWinsData":"0012","LastSaveChapter":"raw/path","PrivateIdentity":"do-not-display"},
             "CloudSettings":{},"LocalSettings":{}}
            """;
        using var document = Open("PPSA02343", "ue4savegame.dpx.sav", source);
        var nodes = OverlayNodes(document);
        Assert.Contains(nodes.OfType<SectionNode>(), n => n.Title == "Plunger");
        Assert.Contains(nodes.OfType<SectionNode>(), n => n.Title == "Cody Wins Data");
        Assert.Contains(nodes.OfType<TextNode>(), n => n.Text == "\"True\"");
        Assert.Contains(nodes.OfType<TextNode>(), n => n.Text == "\"0012\"");
        Assert.DoesNotContain(nodes.OfType<TextNode>(), n => n.Text.Contains("do-not-display", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes, n => n is FieldNode);
        Assert.Contains(nodes.OfType<ActionNode>(), action => action.ActionId == "semantic/present-flags");
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA04027", "settings.json", "Master Volume")]
    [InlineData("PPSA02343", "ue4savegame.dpx.sav", "Chapter and checkpoint strings")]
    [InlineData("PPSA07696", "imgui_emu_option.json", "Upscaling factor")]
    public void EveryRealGenerationHasBoundedRichOverlayPlannedWritesAndIdenticalRoundTrip(string title, string name, string label)
    {
        var files = SampleSaves.Load(title).AllDiskFiles.Where(f => f.FileName == name).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var document = new GenericJsonPlugin().Open(file);
            var nodes = OverlayNodes(document);
            Assert.Contains(nodes.OfType<SectionNode>(), node => node.Title == label);
            Assert.DoesNotContain(nodes, node => node is FieldNode);
            Assert.NotEmpty(nodes.OfType<ActionNode>());
            Assert.All(nodes.OfType<ActionNode>(), action =>
            {
                Assert.True(action.SupportsPlanning);
                Assert.Contains("game may reject", action.Description);
                Assert.All(action.Parameters, parameter => Assert.NotEqual(Confidence.Verified, parameter.Confidence));
            });
            Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
        }
    }

    private static ISaveDocument Open(string title, string name, string source) =>
        new GenericJsonPlugin().Open(MemorySaveSet.SingleFile(name, Encoding.UTF8.GetBytes(source), title));

    private static IReadOnlyList<ViewNode> OverlayNodes(ISaveDocument document) =>
        JsonTestViews.Walk(Assert.IsType<TabsNode>(document.BuildView()).Tabs[3].Children, document.ExpandNode, maxChildren: 128);
}
