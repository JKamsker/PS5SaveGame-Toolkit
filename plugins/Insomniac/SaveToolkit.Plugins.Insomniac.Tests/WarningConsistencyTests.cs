using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class WarningConsistencyTests
{
    [Fact]
    public void EditableFieldsOverviewAndBoltPlanningRetainTheCommonWarningAndFormatQualifications()
    {
        const string warning = "Not validated on console. The game may reject or overwrite the edited save. " +
            "Integrity coverage is unknown. Option unlocks and gameplay dependencies are not changed. " +
            "Owner validation: https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31.";
        var gameplay = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        var preferences = SampleSaves.Load("PPSA01474").AllDiskFiles.First(file =>
            file.FileName == "i29-userprefs.save" && !file.Directory.IsBackup);
        var plugin = new InsomniacPlugin();
        Assert.Contains(warning, plugin.Info.Description);

        foreach (var file in new[] { gameplay, preferences })
        {
            using var document = plugin.Open(file);
            var view = Assert.IsType<TabsNode>(document.BuildView());
            var writable = Fields(view).Where(field => !field.Spec.ReadOnly).ToArray();
            Assert.NotEmpty(writable);
            Assert.All(writable, field =>
            {
                Assert.Equal(warning, field.Warning);
                Assert.Equal(Confidence.Inferred, field.Confidence);
            });
            var overview = Assert.Single(view.Tabs, tab => tab.Title == "Overview");
            Assert.Contains(overview.Children.OfType<TextNode>(), node =>
                node.Text == warning && node.Tone == Tone.Warning);

            var actions = view.Tabs.SelectMany(tab => tab.Children).OfType<ActionNode>().ToArray();
            Assert.Equal(file == gameplay ? 1 : 0, actions.Length);
            foreach (var action in actions)
            {
                Assert.Contains(warning, action.Description);
                Assert.Equal(warning, Assert.Single(action.Parameters).Warning);
                var plan = ((IActionPlanningDocument)document).PlanAction(action.ActionId,
                    new Dictionary<string, FieldValue>(), new());
                Assert.Contains(warning, plan.Warnings);
            }
        }
    }

    private static IEnumerable<FieldNode> Fields(ViewNode node) => node switch
    {
        FieldNode field => [field],
        TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children).SelectMany(Fields),
        SectionNode section => section.Children.SelectMany(Fields),
        _ => [],
    };
}
