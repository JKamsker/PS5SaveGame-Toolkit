using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class PointTargetEligibilityTests
{
    [Fact]
    public void MissingAndUnknownPointRecordsAreSkippedAndNeverCreatedOrChanged()
    {
        var bytes = PointTargetFixtures.RewritePoints((package, records) => records
            .Where(record => record.Enum("type", "gamedataDevelopmentPointType", "Attribute") != "Primary")
            .Select(record =>
            {
                if (record.Enum("type", "gamedataDevelopmentPointType", "Attribute") != "Secondary")
                    return record.Rebuild(new Dictionary<string, byte[]>());
                var type = new byte[2];
                BinaryPrimitives.WriteUInt16LittleEndian(type, package.NameIndex("Cool"));
                var balance = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(balance, 37);
                return record.Rebuild(new Dictionary<string, byte[]> { ["type"] = type, ["unspent"] = balance });
            }));
        using var document = ProgressionSampleTests.Open(bytes);
        var action = Assert.Single(DevelopmentTab(document).Children.OfType<ActionNode>());
        Assert.Equal(2, action.Parameters.Count);
        Assert.DoesNotContain(action.Parameters, p => p.ValueId is "Primary" or "Cool");
        var parameters = PointTargetActionTests.TargetValues();
        parameters.Remove("Primary");
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var plan = planner.PlanAction(PointTargetActionTests.ActionId, parameters, new());
        Assert.Empty(plan.Errors);
        Assert.Equal(2, plan.Changes.Count);
        Assert.Contains(plan.SkippedRows, row => row.RowKey == "Primary" && row.Reason.Contains("absent", StringComparison.Ordinal));
        Assert.Contains(plan.SkippedRows, row => row.RowKey == "Cool" && row.Reason.Contains("Unsupported", StringComparison.Ordinal));
        var invalid = new Dictionary<string, FieldValue>(parameters) { ["Primary"] = FieldValue.Of(1L) };
        Assert.NotEmpty(planner.PlanAction(PointTargetActionTests.ActionId, invalid, new()).Errors);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(planner.ApplyActionPlan(PointTargetActionTests.ActionId, parameters, new(), plan).Succeeded);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        PointTargetFixtures.AssertOnlyUnspentChanges(CsavContainer.Parse(bytes), CsavContainer.Parse(edited), parameters, expectedGrowth: 12);
    }

    [Fact]
    public void NoSupportedBalancesMeansNoActionAndNoPlan()
    {
        var bytes = PointTargetFixtures.RewritePoints((_, records) => records
            .Where(record => record.Enum("type", "gamedataDevelopmentPointType", "Attribute") == "Secondary")
            .Select(record => record.Rebuild(new Dictionary<string, byte[]>())));
        using var document = ProgressionSampleTests.Open(bytes);
        Assert.Empty(DevelopmentTab(document).Children.OfType<ActionNode>());
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var plan = planner.PlanAction(PointTargetActionTests.ActionId, new Dictionary<string, FieldValue>(), new());
        Assert.NotEmpty(plan.Errors);
        Assert.False(planner.ApplyActionPlan(PointTargetActionTests.ActionId, new Dictionary<string, FieldValue>(), new(), plan).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedSaveOrPackageCannotPublishOrApplyPointTargets(bool package)
    {
        var original = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        byte[] bytes;
        if (package)
        {
            var payload = original.Payload.ToArray();
            var node = Assert.Single(CsavTree.Parse(original).Nodes, n => n.Name == "ScriptableSystemsContainer");
            payload[node.Offset + 8] = 5;
            bytes = original.Rebuild(payload);
        }
        else
        {
            bytes = original.Original.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 2220);
        }
        using var document = ProgressionSampleTests.Open(bytes);
        Assert.Empty(DevelopmentTab(document).Children.OfType<ActionNode>());
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = PointTargetActionTests.TargetValues();
        var plan = planner.PlanAction(PointTargetActionTests.ActionId, parameters, new());
        Assert.NotEmpty(plan.Errors);
        Assert.False(planner.ApplyActionPlan(PointTargetActionTests.ActionId, parameters, new(), plan).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static Tab DevelopmentTab(ISaveDocument document) =>
        Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Player development");
}
