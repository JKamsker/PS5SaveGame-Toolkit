using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class HonestLabelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryLegacyScalarWrite_ExplainsSchemaEvidenceAndConsoleRisk(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands, root =>
        {
            var ability = GearboxTestData.AddMessage(root, "ability_data");
            ability.SetInteger("ability_points", 2);
            ability.SetInteger("tree_grade", 1);
            var statistic = GearboxTestData.AddMessage(root, "game_stats_data");
            statistic.SetText("stat_path", CharacterProgression.LevelStat(wonderlands));
            statistic.SetInteger("stat_value", 20);
        });
        var original = Assert.Single(document.Serialize()).Content.ToArray();
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var fields = view.Tabs[0].Children.OfType<FieldNode>().Where(field => !field.Spec.ReadOnly).ToArray();
        Assert.Contains(fields, field => field.ValueId == "character/skill-points");
        Assert.Contains(fields, field => field.ValueId == "character/tree-grade");
        Assert.Contains(fields, field => field.ValueId == "character/recorded-level");
        Assert.Contains(fields, field => field.ValueId == "character/level");
        Assert.Contains(fields, field => field.ValueId == "character/xp");
        foreach (var field in fields.OrderBy(field => field.ValueId == "character/level" ? 0 : 1))
        {
            Assert.Equal(Confidence.Verified, field.Confidence);
            Assert.Contains("Not validated on console", field.Warning);
            Assert.Contains("game may reject or overwrite", field.Warning);
            Assert.Contains("schema", field.Description);
            Assert.Contains("does not establish console acceptance", field.Description, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
