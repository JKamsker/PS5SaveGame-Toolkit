using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>Nonempty authored containers, including opaque fields and trailing bytes; never private saves.</summary>
internal static class ActionEffectData
{
    internal static readonly IReadOnlyDictionary<string, FieldValue> Empty = new Dictionary<string, FieldValue>();
    internal static string Title(bool wonderlands) => wonderlands ? "PPSA01493" : "PPSA01463";

    internal static GearboxDocument Document(bool wonderlands, bool profile = false, int count = 2)
    {
        var container = GearboxContainer.Parse(GearboxTestData.CreateContainer(wonderlands, profile, tail: [0xAB, 0xCD]), Title(wonderlands), profile);
        var root = container.Decode();
        var list = profile ? GearboxInventory.Bank : GearboxInventory.Backpack;
        for (var i = 0; i < count; i++) Add(root, list, Serial(wonderlands), i + 1);
        if (profile) Add(root, GearboxInventory.LostLoot, Serial(wonderlands), 1);
        else
        {
            GearboxTestData.AddMessage(root, "equipped_inventory_list").SetInteger("inventory_list_index", 0);
            GearboxTestData.AddMessage(root, "equipped_inventory_list").SetInteger("inventory_list_index", count - 1);
        }
        // Unknown root bytes must survive all edits, exports and restores.
        root.Add(new ProtoField(10000, 0, null, [], 37UL, null));
        var imported = GearboxContainer.Parse(container.Encode(root), Title(wonderlands), profile);
        return new GearboxDocument(profile ? "profile" : "1", Title(wonderlands), imported, imported.Decode(), IReferenceContext.Empty);
    }

    internal static byte[] Serial(bool wonderlands) => IndependentSerialProbe.Encode(
        SyntheticItem.Payload(wonderlands, additionalData: 255), wonderlands ? 5 : 4, -1);

    internal static void Add(ProtoMessage root, string list, byte[] serial, int pickup)
    {
        var spec = root.Schema.Spec(root.Type, list);
        if (!root.Schema.IsMessage(spec.Type))
        {
            root.Add(new ProtoField(root.Schema.Tag(root.Type, list), 2, spec, [], serial, null));
            return;
        }
        var item = GearboxTestData.AddMessage(root, list);
        item.Add(new ProtoField(root.Schema.Tag(item.Type, "item_serial_number"), 2,
            root.Schema.Spec(item.Type, "item_serial_number"), [], serial, null));
        item.SetInteger("flags", 1);
        if (list == GearboxInventory.Backpack) item.SetInteger("pickup_order_index", pickup);
    }

    internal static MemorySaveSet Set(bool wonderlands)
    {
        using var character = Document(wonderlands);
        using var profile = Document(wonderlands, profile: true);
        return new MemorySaveSet().With(Title(wonderlands), "EFFECTS", new Dictionary<string, byte[]>
        {
            ["1"] = character.Serialize().Single().Content.ToArray(),
            ["profile"] = profile.Serialize().Single().Content.ToArray(),
            ["companion.bin"] = [0xBA, 0xDC, 0x0F, 0xFE],
        });
    }

    internal static GearboxGroupDocument Group(bool wonderlands) => (GearboxGroupDocument)new GearboxPlugin().OpenGroup(
        new(((ISaveSet)Set(wonderlands)).AllFiles.Where(file => file.FileName is "1" or "profile")
            .Select(file => new DocumentGroupFile(file.FileName == "profile" ? "profile" : "character", file)).ToArray()), IReferenceContext.Empty);
    internal static Dictionary<string, byte[]> Bytes(ISaveDocument document) => document.Serialize()
        .ToDictionary(output => output.RelativePath, output => output.Content.ToArray(), StringComparer.Ordinal);

    internal static void AssertIdentity(InventoryIdentity expected, GearboxDocument document)
    {
        var actual = document.Inventory.Capture();
        Assert.Equal(expected.Initialized, actual.Initialized);
        Assert.Equal(expected.NextId, actual.NextId);
        Assert.Equal(expected.Lists.Keys, actual.Lists.Keys);
        foreach (var list in expected.Lists.Keys) Assert.Equal(expected.Lists[list], actual.Lists[list]);
    }

    internal static void AssertBytes(Dictionary<string, byte[]> expected, ISaveDocument document) =>
        SelectionByteAssertions.Equal(expected, Bytes(document));
}
