using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class UnsupportedBoltLayoutTests
{
    [Theory]
    [InlineData("CareerBolts")]
    [InlineData("SpentBolts")]
    public void UnsupportedCounterWireTypeKeepsTheEntireBoltPairReadOnly(string name)
    {
        var source = PlayerCorpusTests.Find("i29slot0-s-autosave", 1);
        var bytes = source.ReadAllBytes();
        ChangeCounterKind(bytes, name, DdlValueKind.Int16);
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("i29-s.save", bytes, "PPSA01474"));
        var view = document.BuildView();
        // The positive signed value remains inspectable, but is outside the unsigned write policy.
        var fields = EagerViewFields.Enumerate(view).ToDictionary(field => field.ValueId);
        Assert.True(document.CanEdit); // Independent gold-bolt booleans remain available.
        var otherId = name == "CareerBolts" ? RiftScalarWrites.SpentId : RiftScalarWrites.CareerId;
        var negativeValue = name == "CareerBolts" ? fields[RiftScalarWrites.CareerId].Value.AsInteger() + 1 : 0;
        var rejected = document.ApplyEdits([
            new("player/RPGEnabled", FieldValue.Of(!fields["player/RPGEnabled"].Value.AsBool())),
            new(otherId, FieldValue.Of(negativeValue))]);
        Assert.False(rejected.Succeeded);
        Assert.Empty(rejected.Changes);
        Assert.Equal(bytes, Output(document));
        Assert.True(fields[RiftScalarWrites.CareerId].Spec.ReadOnly);
        Assert.True(fields[RiftScalarWrites.SpentId].Spec.ReadOnly);
        Assert.Empty(Assert.IsType<TabsNode>(view).Tabs.SelectMany(tab => tab.Children).OfType<ActionNode>());
        var planner = (IActionPlanningDocument)document;
        var parameters = new Dictionary<string, FieldValue> { ["target"] = FieldValue.Of(0L) };
        var plan = planner.PlanAction(RiftBoltPlanning.ActionId, parameters, new());
        Assert.NotEmpty(plan.Errors);
        Assert.False(planner.ApplyActionPlan(RiftBoltPlanning.ActionId, parameters, new(), plan).Succeeded);
        Assert.Equal(bytes, Output(document));
        var oldOption = fields["player/RPGEnabled"].Value;
        Assert.True(document.ApplyEdits([new("player/RPGEnabled", FieldValue.Of(!oldOption.AsBool()))]).Succeeded);
        Assert.True(document.ApplyEdits([new("player/RPGEnabled", oldOption)]).Succeeded);
        Assert.Equal(bytes, Output(document));
    }

    private static void ChangeCounterKind(byte[] bytes, string name, DdlValueKind kind)
    {
        var container = Dat1Container.Parse(bytes);
        var section = Assert.Single(container.Sections, candidate => candidate.Tag == RiftPlayerView.CampaignTag);
        var start = checked((int)section.Offset);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(start + 8));
        var hash = DdlReader.NameHash(Encoding.UTF8.GetBytes(name));
        for (var index = 0; index < count; index++)
        {
            var descriptor = start + 16 + index * 8;
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(descriptor)) != hash) continue;
            var meta = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(descriptor + 4));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(descriptor + 4), (meta & 0xFFFFFF) | ((uint)kind << 24));
            return;
        }
        Assert.Fail($"Counter {name} was not found.");
    }

    private static byte[] Output(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
}
