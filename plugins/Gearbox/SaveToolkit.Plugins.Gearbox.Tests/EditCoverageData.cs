using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Gearbox.Tests;

internal static class EditCoverageData
{
    internal const string AmmoPath = "/Game/GameData/Weapons/Ammo/Resource_Ammo_Pistol.Resource_Ammo_Pistol";
    internal const string OtherAmmoPath = "/Game/GameData/Weapons/Ammo/Resource_Ammo_SMG.Resource_Ammo_SMG";

    internal static GearboxDocument Create(bool wonderlands, Action<ProtoMessage>? configure = null, bool profile = false,
        bool coordinated = false)
    {
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        var container = GearboxContainer.Parse(GearboxTestData.CreateContainer(wonderlands, profile, tail: [0xAB, 0xCD]), title, profile);
        var root = container.Decode();
        if (!profile)
        {
            root.SetInteger("experience_points", root.Schema.Experience[19]);
            AddItem(root, 0x101);
            AddItem(root, 7);
            AddItem(root, 1, [4, 0]);
            AddPool(root, AmmoPath, 123.5f);
            AddPool(root, OtherAmmoPath, 50);
        }
        configure?.Invoke(root);
        var imported = GearboxContainer.Parse(container.Encode(root), title, profile);
        return new GearboxDocument(profile ? "profile" : "1", title, imported, imported.Decode(), IReferenceContext.Empty)
        { Coordinated = coordinated };
    }

    internal static void AddItem(ProtoMessage root, long flags, byte[]? bytes = null)
    {
        var item = GearboxTestData.AddMessage(root, GearboxInventory.Backpack);
        item.SetInteger("flags", flags);
        var serial = bytes ?? IndependentSerialProbe.Encode(SyntheticItem.Payload(root.Schema.Wonderlands), root.Schema.Wonderlands ? 5 : 4, -1);
        item.Add(new ProtoField(root.Schema.Tag(item.Type, "item_serial_number"), 2,
            root.Schema.Spec(item.Type, "item_serial_number"), [], serial, null));
    }

    internal static void AddPool(ProtoMessage root, string path, float amount)
    {
        var pool = GearboxTestData.AddMessage(root, "resource_pools");
        pool.SetText("resource_path", path);
        pool.SetFloat("amount", amount);
    }
}
