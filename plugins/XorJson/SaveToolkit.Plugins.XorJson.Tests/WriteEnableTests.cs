using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class WriteEnableTests
{
    [Fact]
    public void TestedOfflineCharacterScalarEditReopensAndWarns()
    {
        var file = SampleSaves.Load("PPSA16064").AllDiskFiles.Single(file => !file.IsSystemFile && file.Directory.Name.StartsWith("Character", StringComparison.Ordinal));
        var plugin = new XorJsonPlugin();
        using var document = plugin.Open(file);
        var level = Fields(document).Single(field => field.Label == "Level" && field.Description!.Contains("MetaData", StringComparison.Ordinal));
        var original = file.ReadAllBytes();
        var result = document.ApplyEdits([new(level.ValueId, FieldValue.ExactNumber("2"))]);
        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings, warning => warning.Contains("console", StringComparison.OrdinalIgnoreCase));
        var output = document.Serialize().Single().Content.ToArray();
        Assert.NotEqual(original, output);
        using var reopened = plugin.Open(MemorySaveSet.SingleFile(file.RelativePath, output, file.Directory.TitleId));
        var reopenedLevel = Fields(reopened).Single(field => field.Label == "Level" && field.Description!.Contains("MetaData", StringComparison.Ordinal));
        Assert.Equal("2", reopenedLevel.Value.AsExactNumber());
        Assert.Equal(output, reopened.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void BulkPlanListsWarningsAndSkipsIncompatibleRows()
    {
        using var document = XorJsonTests.Open(XorJsonTests.Encode("{\"a\":1,\"b\":true,\"nested\":{\"c\":2}}"));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var fields = XorJsonTests.Fields(document);
        var number = fields.Single(field => field.Label == "a");
        var boolean = fields.Single(field => field.Label == "b");
        var plan = planner.PlanAction("json/bulk-set", new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of("3") },
            new ActionScope([number.ValueId, boolean.ValueId]));
        Assert.Empty(plan.Errors);
        Assert.Single(plan.Changes);
        Assert.Single(plan.SkippedRows);
        Assert.Contains(plan.Warnings, warning => warning.Contains("Experimental", StringComparison.Ordinal));
        var applied = planner.ApplyActionPlan("json/bulk-set", new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of("3") },
            new ActionScope([number.ValueId, boolean.ValueId]), plan);
        Assert.Empty(applied.Errors);
        Assert.Equal("3", document.GetValue(number.ValueId).AsExactNumber());
    }

    [Fact]
    public void CharacterGlobalGroupStagesBothMembersAndRestoresOneMemento()
    {
        var sample = SampleSaves.Load("PPSA16064");
        var files = sample.AllDiskFiles.Where(file => !file.IsSystemFile).ToArray();
        var character = files.Single(file => file.Directory.Name.StartsWith("Character", StringComparison.Ordinal));
        var global = files.Single(file => file.Directory.Name == "GlobalSaveDataDefault");
        var plugin = new XorJsonPlugin();
        var proposal = Assert.Single(plugin.ProposeGroups(character));
        Assert.Empty(proposal.Errors);
        using var group = plugin.OpenGroup(new([
            new DocumentGroupFile("character", character), new DocumentGroupFile("global", global),
        ]), IReferenceContext.Empty);
        Assert.True(group.CanEdit);
        var fields = Fields(group);
        var level = fields.Single(field => field.ValueId.StartsWith("character/", StringComparison.Ordinal) && field.Label == "Level" && field.Description!.Contains("MetaData", StringComparison.Ordinal));
        var volume = fields.Single(field => field.ValueId.StartsWith("global/", StringComparison.Ordinal) && field.Label == "masterVolume");
        var before = group.CaptureMemento();
        var result = group.ApplyEdits([
            new(level.ValueId, FieldValue.ExactNumber("2")), new(volume.ValueId, FieldValue.ExactNumber("49")),
        ]);
        Assert.True(result.Succeeded);
        Assert.Equal(2, group.Serialize().Count);
        Assert.Contains(result.Warnings, warning => warning.Contains("transaction", StringComparison.OrdinalIgnoreCase));
        _ = group.RestoreMemento(before);
        var restored = group.Serialize().ToDictionary(output => output.RelativePath, output => output.Content.ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(character.ReadAllBytes(), restored[character.RelativePath]);
        Assert.Equal(global.ReadAllBytes(), restored[global.RelativePath]);
    }

    private static IReadOnlyList<FieldNode> Fields(ISaveDocument document)
    {
        var fields = new List<FieldNode>();
        Visit(document.BuildView());
        return fields;
        void Visit(ViewNode node)
        {
            if (node is FieldNode field) fields.Add(field);
            if (node is TabsNode tabs) foreach (var tab in tabs.Tabs) foreach (var child in tab.Children) Visit(child);
            if (node is SectionNode section)
                foreach (var child in section.LazyId is { } id ? document.ExpandNode(id) : section.Children) Visit(child);
        }
    }
}
