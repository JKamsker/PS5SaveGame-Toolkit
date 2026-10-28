using System.Text;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonOverlayTests
{
    [Theory]
    [InlineData("PPSA00000", "settings.json", "{\"version\":136,\"data\":[]}")]
    [InlineData("PPSA04027", "metadata.json", "{\"version\":136,\"data\":[]}")]
    [InlineData("PPSA04027", "settings.json", "{\"version\":137,\"data\":[]}")]
    [InlineData("PPSA04027", "settings.json", "{\"version\":136,\"version\":136,\"data\":[]}")]
    [InlineData("PPSA04027", "settings.json", "{\"version\":\"136\",\"data\":[]}")]
    [InlineData("PPSA02343", "ue4savegame.dpx.sav", "{\"Profile\":{\"Minigame.X\":false},\"CloudSettings\":{},\"LocalSettings\":{}}")]
    public void UnmatchedTitleRoleVersionOrAmbiguousShapeRetainsGenericFallback(string title, string name, string source)
    {
        using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile(name, Encoding.UTF8.GetBytes(source), title));
        Assert.Equal(3, Assert.IsType<TabsNode>(document.BuildView()).Tabs.Count);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void ScalarTypeChangeInvalidatesOverlayEvenWhenDisplayedTextMatches()
    {
        using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("settings.json", "{\"version\":136,\"data\":[]}"u8.ToArray(), "PPSA04027"));
        Assert.Equal(4, Assert.IsType<TabsNode>(document.BuildView()).Tabs.Count);
        var field = JsonEditingTests.Fields(document).Single(f => f.Label == "version");
        var edit = document.ApplyEdits([new(field.ValueId, JsonEditingTests.Text("136"))]);
        Assert.True(edit.Succeeded);
        Assert.True(edit.StructureChanged);
        Assert.Equal(3, Assert.IsType<TabsNode>(document.BuildView()).Tabs.Count);
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(4, Assert.IsType<TabsNode>(document.BuildView()).Tabs.Count);
    }

    [Fact]
    public void SameTypeVersionEditRefreshesOverlayMatchingAndUndo()
    {
        using var fixture = new JsonSessionFixture("{\"version\":137,\"data\":[]}", title: "PPSA04027", fileName: "settings.json");
        var version = fixture.Fields().Single(f => f.Label == "version");
        var edited = fixture.Edit(version.ValueId, JsonEditingTests.Number("136"));
        Assert.True(edited.Ok);
        Assert.True(edited.StructureChanged);
        Assert.Equal(4, Assert.IsType<TabsNode>(fixture.Session.BuildView()).Tabs.Count);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(3, Assert.IsType<TabsNode>(fixture.Session.BuildView()).Tabs.Count);
    }
}
