using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class WriteAtomicityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverCapacitySduRollback_RestoresTheEntireRejectedBatch(bool wonderlands)
    {
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        var container = GearboxContainer.Parse(ContainerTests.Create(wonderlands, false), title, false);
        var root = container.Decode();
        var sdu = AddMessage(root, "sdu_list");
        sdu.SetText("sdu_data_path", "/Game/Pickups/SDU/SDU_Backpack.SDU_Backpack");
        sdu.SetInteger("sdu_level", 1);
        // Imported over-capacity inventories are preserved, even when growth is disallowed.
        const int overCapacityCount = 20;
        for (var i = 0; i < overCapacityCount; i++) _ = AddMessage(root, GearboxInventory.Backpack);
        _ = AddMessage(root, "ability_data");
        using var document = new GearboxDocument("1", title, container, root, IReferenceContext.Empty);
        _ = document.BuildView();
        _ = document.ExpandNode("sdus");
        var original = document.Serialize().Single().Content.ToArray();
        var identities = document.Inventory.Entries.Select(e => e.Id).ToArray();
        EditOutcome? rejected = null;

        var exception = Record.Exception(() => rejected = document.ApplyEdits([
            new("character/level", FieldValue.Of(20)),
            new("currency/618814354", FieldValue.Of(123)),
            new("sdu/0", FieldValue.Of(0)),
        ]));

        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.Equal(identities, document.Inventory.Entries.Select(e => e.Id));
        Assert.Null(exception);
        Assert.NotNull(rejected);
        Assert.False(rejected.Succeeded);
        Assert.Empty(rejected.Changes);
        Assert.Null(rejected.Revert);
        // A rejected level increase must not consume the Wonderlands award high-water mark.
        Assert.True(document.ApplyEdits([new("character/level", FieldValue.Of(2))]).Succeeded);
        Assert.Equal(wonderlands ? 1 : 0, document.Root.Child("ability_data")!.Integer("ability_points"));
    }

    internal static ProtoMessage AddMessage(ProtoMessage parent, string name)
    {
        var spec = parent.Schema.Spec(parent.Type, name);
        var child = new ProtoMessage(parent.Schema, spec.Type, []);
        parent.Add(new ProtoField(parent.Schema.Tag(parent.Type, name), 2, spec, [], Array.Empty<byte>(), child));
        return child;
    }
}
