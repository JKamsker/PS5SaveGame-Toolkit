using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class GroupPlanningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothMembersPlanApplyAndRestoreWithRolePrefixedIdentities(bool xor)
    {
        var set = new MemorySaveSet()
            .With("PPSA16064", "Character0", new Dictionary<string, byte[]>
            {
                ["hero.sav"] = Encoding.UTF8.GetBytes("{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\",\"SoftVersion\":2,\"FormatHash\":3777917472},\"CharacterSaveV1\":{\"MetaData\":{\"IsOnline\":false,\"Level\":1}}}"),
            })
            .With("PPSA16064", "GlobalSaveDataDefault", new Dictionary<string, byte[]>
            {
                ["global.sav"] = XorJsonTests.Encode("{\"blobs\":[],\"volume\":50,\"future\":-0.00E+02}", xor),
            });
        var files = set.Directories.SelectMany(directory => directory.Files).ToArray();
        using var group = new XorJsonPlugin().OpenGroup(new([new("character", files[0]), new("global", files[1])]), IReferenceContext.Empty);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(group);
        var fields = StructuralTests.Nodes(group).OfType<FieldNode>().ToArray();
        foreach (var (role, label, next) in new[] { ("character", "Level", "2.00E+00"), ("global", "volume", "49.00E+00") })
        {
            var field = fields.Single(field => field.Label == label);
            var parameters = new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of(next) };
            var scope = new ActionScope([field.ValueId]);
            var plan = planner.PlanAction(role + "/json/bulk-set", parameters, scope);
            Assert.Empty(plan.Errors);
            var change = Assert.Single(plan.Changes);
            Assert.Equal(field.ValueId, change.ValueId);
            Assert.Equal(field.Value, change.OldValue);
            Assert.Equal(FieldValue.ExactNumber(next), change.NewValue);
            var outcome = planner.ApplyActionPlan(role + "/json/bulk-set", parameters, scope, plan);
            Assert.True(outcome.Succeeded);
            Assert.Equal(change.NewValue, group.GetValue(field.ValueId));
            Assert.Equal(field.ValueId, Assert.Single(outcome.Changes).ValueId);
            Assert.Contains(outcome.Warnings, warning => warning.StartsWith("Experimental character/global transaction.", StringComparison.Ordinal));
            _ = group.RestoreMemento(outcome.Revert!);
            foreach (var output in group.Serialize())
                Assert.Equal(files.Single(file => file.RelativePath == output.RelativePath).ReadAllBytes(), output.Content.ToArray());
        }
    }
}
