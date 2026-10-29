using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonOverlayGuardTests
{
    private const string CyberpunkTitle = "PPSA04027";
    private const string ProfileTitle = "PPSA02343";
    private const string WrapperTitle = "PPSA07696";
    private const string CyberpunkTab = "Cyberpunk settings (version 136)";
    private const string ProfileTab = "It Takes Two profile";
    private const string WrapperTab = "Ridge Racer emulator options";
    private const int MaxOverlayEntries = 128;
    private const string Group = "{\"group_name\":\"/test\",\"options\":[]}";
    private const string Cyberpunk = "{\"version\":136,\"data\":[" + Group + "]}";
    private const string Profile = "{\"Profile\":{\"Minigame.X\":\"true\"},\"CloudSettings\":{},\"LocalSettings\":{}}";
    private const string Wrapper = "{\"options\":{\"enabled\":true},\"settings\":{}}";
    private static readonly string[] GenericTabs = ["Overview", "JSON", "Search"];

    public static TheoryData<string, string, string, string> BundledOverlays => new()
    {
        { CyberpunkTitle, "settings.json", Cyberpunk, CyberpunkTab },
        { ProfileTitle, "ue4savegame.dpx.sav", Profile, ProfileTab },
        { WrapperTitle, "imgui_emu_option.json", Wrapper, WrapperTab },
    };

    public static IEnumerable<object[]> RejectedMatches()
    {
        (string Title, string Name, string Source)[] cases =
        [
            (CyberpunkTitle, "settings.json", Cyberpunk),
            (ProfileTitle, "ue4savegame.dpx.sav", Profile),
            (WrapperTitle, "imgui_emu_option.json", Wrapper),
        ];
        foreach (var (title, name, source) in cases)
        {
            yield return ["PPSA00000", name, source];
            yield return [title, "unrelated.json", source];
            foreach (var foreign in cases.Where(c => c.Title != title))
                yield return [foreign.Title, name, source];
            yield return [title, name, "[]"];

            using var json = JsonDocument.Parse(source);
            var members = json.RootElement.EnumerateObject().ToArray();
            foreach (var member in members)
            {
                var other = string.Join(',', members.Where(p => p.Name != member.Name).Select(p => p.ToString()));
                yield return [title, name, "{" + other + "}"];
                // Equivalent escaped names are also ambiguous, even with identical values.
                var escapedName = "\\u" + ((int)member.Name[0]).ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + member.Name[1..];
                yield return [title, name, source[..^1] + ",\"" + escapedName + "\":" + member.Value.GetRawText() + "}"];
                // Profile companion shapes are deliberately unconstrained; only their presence is required.
                if (title == ProfileTitle && member.Name != "Profile") continue;
                yield return [title, name, "{" + other + ",\"" + member.Name + "\":null}"];
                yield return [title, name, "{" + other + ",\"" + member.Name + "\":false}"];
                var wrongContainer = title == CyberpunkTitle && member.Name == "data" ? "{}" : "[]";
                yield return [title, name, "{" + other + ",\"" + member.Name + "\":" + wrongContainer + "}"];
            }
        }
    }

    [Theory]
    [MemberData(nameof(BundledOverlays))]
    public void EveryBundledOverlayHasAnExactSampleFreePositiveControl(string title, string name, string source, string tab)
    {
        using var document = Open(title, name, source);
        AssertTabs(document, tab);
        var fields = JsonEditingTests.Fields(document);
        Assert.NotEmpty(fields);
        var actions = OverlayActions(document, tab);
        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.True(action.SupportsPlanning));
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [MemberData(nameof(RejectedMatches))]
    public void WrongTitlesRolesAndAmbiguousOrInvalidRequiredMembersRetainRawAccess(string title, string name, string source)
    {
        using var document = Open(title, name, source);
        AssertTabs(document);
        var fields = JsonEditingTests.Fields(document);
        using var json = JsonDocument.Parse(source);
        Assert.Equal(ScalarCount(json.RootElement), fields.Count);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
        // A declined overlay is still editable JSON, including duplicate required-name occurrences.
        if (fields.Count == 0) return;
        var field = fields[0];
        Assert.True(document.ApplyEdits([new(field.ValueId, JsonEditingTests.Text("raw edit"))]).Succeeded);
        Assert.Equal(JsonEditingTests.Text("raw edit"), document.GetValue(field.ValueId));
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData("{\"group_name\":\"/test\",\"options\":null}")]
    [InlineData("{\"group_name\":false,\"options\":[]}")]
    [InlineData("{\"group_name\":\"/test\",\"group_name\":\"/test\",\"options\":[]}")]
    [InlineData("{\"group_name\":\"/test\",\"options\":[],\"options\":[]}")]
    public void MalformedCyberpunkGroupDeclinesTheWholeOverlay(string group)
    {
        var source = "{\"version\":136,\"data\":[" + Group + "," + group + "]}";
        using var document = Open(CyberpunkTitle, "settings.json", source);
        AssertTabs(document);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData(MaxOverlayEntries, true)]
    [InlineData(MaxOverlayEntries + 1, false)]
    public void CyberpunkGroupLimitHasAnAcceptedEndpoint(int count, bool matched)
    {
        var source = "{\"version\":136,\"data\":[" + string.Join(',', Enumerable.Repeat(Group, count)) + "]}";
        using var document = Open(CyberpunkTitle, "settings.json", source);
        AssertTabs(document, matched ? CyberpunkTab : null);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData(MaxOverlayEntries, true)]
    [InlineData(MaxOverlayEntries + 1, false)]
    public void ProfileLimitCountsGameplayFieldsAndLeavesUnrelatedStringsOut(int count, bool matched)
    {
        var gameplay = string.Join(',', Enumerable.Range(0, count).Select(i => $"\"Minigame.X{i}\":\"true\""));
        var source = "{\"Profile\":{" + gameplay + ",\"Unrelated\":\"kept\"},\"CloudSettings\":null,\"LocalSettings\":false}";
        using var document = Open(ProfileTitle, "ue4savegame.dpx.sav", source);
        AssertTabs(document, matched ? ProfileTab : null);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData(CyberpunkTitle, "settings.json", Cyberpunk, "version", CyberpunkTab)]
    [InlineData(ProfileTitle, "ue4savegame.dpx.sav", Profile, "Minigame.X", ProfileTab)]
    [InlineData(WrapperTitle, "imgui_emu_option.json", Wrapper, "enabled", WrapperTab)]
    public void RawTypeEditsRematchEachOverlayAndHistoryRestoresExactBytes(string title, string name, string source, string label, string tab)
    {
        using var fixture = new JsonSessionFixture(source, title: title, fileName: name);
        var field = fixture.Fields().Single(f => f.Label == label);
        AssertTabs(fixture.Document, tab);
        var next = title == WrapperTitle ? FieldValue.Null : FieldValue.Union("boolean", FieldValue.Of(false));
        var result = fixture.Edit(field.ValueId, next);
        Assert.True(result.Ok);
        var after = fixture.Bytes;
        // The wrapper accepts arbitrary child types; profile and version guards reject the other edits.
        AssertTabs(fixture.Document, title == WrapperTitle ? tab : null);
        if (title == WrapperTitle)
            Assert.DoesNotContain(JsonSemanticTestHelper.Actions(fixture), a => a.ActionId.EndsWith("/set", StringComparison.Ordinal));
        Assert.True(fixture.Session.Undo().Ok);
        AssertTabs(fixture.Document, tab);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(after, fixture.Bytes);
        AssertTabs(fixture.Document, title == WrapperTitle ? tab : null);
    }

    [Theory]
    [MemberData(nameof(BundledOverlays))]
    public void RemovingARequiredRootMemberDisablesEachOverlayUntilStructuralUndo(string title, string name, string source, string tab)
    {
        using var fixture = new JsonSessionFixture(source, title: title, fileName: name);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(fixture.Document);
        var requests = new List<(string Id, Dictionary<string, FieldValue> Values)>();
        foreach (var action in JsonSemanticTestHelper.Actions(fixture).Where(a => a.ActionId.StartsWith("semantic/", StringComparison.Ordinal)))
        {
            var values = action.Parameters.ToDictionary(p => p.ValueId, p => p.Value, StringComparer.Ordinal);
            if (planner.PlanAction(action.ActionId, values, new()).Errors.Count == 0) requests.Add((action.ActionId, values));
        }
        Assert.NotEmpty(requests);
        JsonArrayEditingTests.Apply(fixture, "object/0/remove", ("at", FieldValue.Of(0L)));
        AssertTabs(fixture.Document);
        Assert.All(requests, request => Assert.NotEmpty(planner.PlanAction(request.Id, request.Values, new()).Errors));
        var after = fixture.Bytes;
        Assert.True(fixture.Session.Undo().Ok);
        AssertTabs(fixture.Document, tab);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
        Assert.True(fixture.Session.Redo().Ok);
        AssertTabs(fixture.Document);
        Assert.Equal(after, fixture.Bytes);
    }

    private static ISaveDocument Open(string title, string name, string source) =>
        new GenericJsonPlugin().Open(MemorySaveSet.SingleFile(name, Encoding.UTF8.GetBytes(source), title));

    private static int ScalarCount(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Sum(p => ScalarCount(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Sum(ScalarCount),
        _ => 1,
    };

    private static void AssertTabs(ISaveDocument document, string? overlay = null) => Assert.Equal(
        overlay is null ? GenericTabs : [.. GenericTabs, overlay],
        Assert.IsType<TabsNode>(document.BuildView()).Tabs.Select(t => t.Title));

    private static IReadOnlyList<ActionNode> OverlayActions(ISaveDocument document, string title)
    {
        var nodes = Assert.IsType<TabsNode>(document.BuildView()).Tabs.Single(t => t.Title == title).Children;
        var actions = new List<ActionNode>();
        foreach (var node in nodes) Visit(node);
        return actions;

        void Visit(ViewNode node)
        {
            if (node is ActionNode action) actions.Add(action);
            if (node is SectionNode section)
            {
                var children = section.LazyId is { } id ? document.ExpandNode(id) : section.Children;
                Assert.InRange(children.Count, 0, MaxOverlayEntries);
                foreach (var child in children) Visit(child);
            }
        }
    }
}
