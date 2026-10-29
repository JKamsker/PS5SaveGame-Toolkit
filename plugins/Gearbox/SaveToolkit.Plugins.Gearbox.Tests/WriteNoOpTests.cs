using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class WriteNoOpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdenticalMythAllocation_PreservesNoncanonicalWireBytes(bool packed)
    {
        byte[] wire = packed ? [0x1A, 0x04, 0x81, 0x00, 0x02, 0x03] : [0x18, 0x81, 0x00, 0x18, 0x02, 0x18, 0x03];
        var message = ProtoMessage.Parse(wire, GearboxSchema.Tina, "PlayerPrestigeProfileData");
        ProtoArrayEdits.Set(message, "points_spent_by_index_order", 0, 1);
        Assert.Equal(wire, message.Encode());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ZeroAmmoEdit_PreservesImportedNegativeZeroWithoutUntrackedWrites(bool wonderlands, bool roundTrip)
    {
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        var container = GearboxContainer.Parse(ContainerTests.Create(wonderlands, false), title, false);
        var root = container.Decode();
        var pool = WriteAtomicityTests.AddMessage(root, "resource_pools");
        const uint negativeZeroBits = 0x80000000;
        pool.Add(new ProtoField(root.Schema.Tag(pool.Type, "amount"), 5, root.Schema.Spec(pool.Type, "amount"), [], negativeZeroBits, null));
        // Reparse to establish the imported wire spelling, including the zero's sign bit.
        var imported = GearboxContainer.Parse(container.Encode(root), title, false);
        using var document = new GearboxDocument("1", title, imported, imported.Decode(), IReferenceContext.Empty);
        _ = document.BuildView();
        _ = document.ExpandNode("ammo");
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(document.GetValue("ammo/0").AsDouble()));

        ValueEdit[] edits = roundTrip ? [new("ammo/0", FieldValue.Of(1.0)), new("ammo/0", FieldValue.Of(0.0))]
            : [new("ammo/0", FieldValue.Of(0.0))];
        var outcome = document.ApplyEdits(edits);

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Changes);
        Assert.Null(outcome.Revert);
        Assert.Equal(imported.Original, document.Serialize().Single().Content.ToArray());

        var changed = document.ApplyEdits([new("ammo/0", FieldValue.Of(1.0))]);
        Assert.True(changed.Succeeded);
        Assert.NotNull(changed.Revert);
        _ = document.RestoreMemento(changed.Revert);
        Assert.Equal(imported.Original, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupMythNoOp_PreservesWireBytesWithoutHistory(bool roundTrip)
    {
        const string title = "PPSA01493";
        const string id = "profile/account/myth/0/points";
        var container = GearboxContainer.Parse(ContainerTests.Create(true, true), title, true);
        var root = container.Decode();
        var spec = root.Schema.Spec(root.Type, "player_prestige");
        var prestige = ProtoMessage.Parse([0x1A, 0x04, 0x81, 0x00, 0x02, 0x03], root.Schema, spec.Type);
        root.Add(new ProtoField(root.Schema.Tag(root.Type, "player_prestige"), 2, spec, [], Array.Empty<byte>(), prestige));
        var set = new MemorySaveSet().With(title, "CHAR", new Dictionary<string, byte[]> { ["1"] = ContainerTests.Create(true, false) })
            .With(title, "PROFILE", new Dictionary<string, byte[]> { ["profile"] = container.Encode(root) });
        var plugin = new GearboxPlugin();
        var input = GearboxGroupTests.Input(set, Assert.Single(plugin.ProposeGroups(set.Directories[0].Files[0])));
        using var group = plugin.OpenGroup(input, IReferenceContext.Empty);
        _ = group.BuildView();
        _ = group.ExpandNode("profile/account/myth/allocations");
        var original = group.Serialize().ToDictionary(o => o.RelativePath, o => o.Content.ToArray());
        ValueEdit[] edits = roundTrip ? [new(id, FieldValue.Of(2)), new(id, FieldValue.Of(1))] : [new(id, FieldValue.Of(1))];

        var outcome = group.ApplyEdits(edits);

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Changes);
        Assert.Null(outcome.Revert);
        Assert.False(outcome.StructureChanged);
        Assert.All(group.Serialize(), output => Assert.Equal(original[output.RelativePath], output.Content.ToArray()));
    }
}
