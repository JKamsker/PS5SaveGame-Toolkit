using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

/// <summary>Authored inventory records, including deliberately ambiguous tag layouts.</summary>
internal static class InventoryFixture
{
    internal static byte[] Create(bool duplicateContainer = false, bool duplicateCollection = false,
        string amountType = "IntProperty", string? unsignedField = null, ulong unsignedValue = 0)
    {
        return CreateSlot(payload =>
        {
            Container(payload);
            if (duplicateContainer) Container(payload);
            EndObject(payload);
        });

        void Container(BinaryWriter target) => Property(target, "ResourcesList", "StructProperty", "InventoryList", list =>
        {
            Slots(list);
            if (duplicateCollection) Slots(list);
            PlainFixture.End(list);
        });

        void Slots(BinaryWriter target) => Property(target, "Slots", "ArrayProperty", "StructProperty", array =>
        {
            array.Write(2);
            Property(array, "Slots", "StructProperty", "InventoryData", elements =>
            {
                Element(elements, "DA_Item_Shved", 1);
                Element(elements, "DA_Item_ShotgunKS23", 2);
            });
        });

        void Element(BinaryWriter target, string asset, long instance)
        {
            Property(target, "ItemDataAsset", "ObjectProperty", null,
                value => SlotFixture.String(value, "/Game/Authored/" + asset + "." + asset));
            if (unsignedField == "Amount")
                Property(target, "Amount", "UInt64Property", null, value => value.Write(unsignedValue));
            else Property(target, "Amount", amountType, null, value => value.Write(4));
            if (unsignedField == "ItemId")
                Property(target, "ItemId", "UInt64Property", null, value => value.Write(unsignedValue));
            else Property(target, "ItemId", "Int64Property", null, value => value.Write(instance));
            PlainFixture.End(target);
        }
    }

    private static byte[] CreateSlot(Action<BinaryWriter> writeInventory)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0); // Decoded array length.
        EndObject(writer);
        writer.Write(0); // Subsystems.
        SlotFixture.String(writer, "AuthoredLevel");
        writer.Write(1); // Actors.
        Record(writer, "Actor", "/Script/Test.Actor", EndObject);
        writer.Write((byte)0); writer.Write((byte)0);
        writer.Write(new byte[40]); writer.Write(0); // Transform and no velocity.
        writer.Write(1); // Components.
        Record(writer, "Inventory", InventoryClasses.Storage, writeInventory);
        writer.Write(new byte[40]); // Component transform.
        writer.Write(0); // Sublevels.
        writer.Flush();
        var plain = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(plain, plain.Length - sizeof(int));
        return SlotFixture.Create(plain: plain);
    }

    private static void Record(BinaryWriter writer, string name, string classPath, Action<BinaryWriter> write)
    {
        SlotFixture.String(writer, name);
        SlotFixture.String(writer, classPath);
        SlotFixture.String(writer, "");
        var payload = Bytes(write);
        writer.Write(payload.Length); writer.Write(payload);
        writer.Write(0); // Tags.
    }

    private static void Property(BinaryWriter writer, string name, string type, string? detail, Action<BinaryWriter> write)
    {
        var payload = Bytes(write);
        SlotFixture.String(writer, name); SlotFixture.String(writer, type);
        writer.Write(payload.Length); writer.Write(0);
        if (detail is not null) SlotFixture.String(writer, detail);
        if (type == "StructProperty") writer.Write(new byte[16]);
        writer.Write((byte)0); // No property GUID.
        writer.Write(payload);
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) write(writer);
        return stream.ToArray();
    }

    private static void EndObject(BinaryWriter writer)
    {
        PlainFixture.End(writer);
        writer.Write(0);
    }
}
