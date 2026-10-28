using System.Text;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

/// <summary>Entirely authored data, never extracted from a private sample.</summary>
internal static class PlainFixture
{
    internal static byte[] Create(bool nativeTail = false, bool secondClassPath = false, long amount = 4,
        bool reversed = false, int repeatedRecords = 1, bool duplicateProperties = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0); // Exact decoded byte-array length is filled below.
        Property(writer, "TestRoot", "IntProperty", w => w.Write(42));
        End(writer); writer.Write(0); // UObject trailing marker.
        writer.Write(repeatedRecords);
        for (var index = 0; index < repeatedRecords; index++)
        {
            SlotFixture.String(writer, "SyntheticSubsystem" + index);
            SlotFixture.String(writer, secondClassPath ? "None" : "/Script/Test.Subsystem");
            SlotFixture.String(writer, secondClassPath ? "/Script/Test.Subsystem" : "");
            using var payload = new MemoryStream();
            using (var data = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
            {
                if (reversed) Property(data, "Other", "IntProperty", w => w.Write(9));
                Property(data, "Amount", "Int64Property", w => w.Write(amount));
                if (duplicateProperties) Property(data, "Amount", "Int64Property", w => w.Write(amount + 1));
                if (!reversed) Property(data, "Other", "IntProperty", w => w.Write(9));
                End(data); data.Write(0);
                if (nativeTail) data.Write(new byte[] { 0xAA, 0xBB, 0xCC });
            }
            writer.Write((int)payload.Length); writer.Write(payload.ToArray());
            writer.Write(1); SlotFixture.String(writer, "SyntheticTag");
        }
        SlotFixture.String(writer, "Persistent"); writer.Write(0); // Main level and actors.
        writer.Write(0); // Sublevels.
        writer.Flush();
        var bytes = stream.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, bytes.Length - sizeof(int));
        return bytes;
    }

    internal static void Property(BinaryWriter writer, string name, string type, Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var value = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) write(value);
        SlotFixture.String(writer, name); SlotFixture.String(writer, type);
        writer.Write((int)stream.Length); writer.Write(0); writer.Write((byte)0); writer.Write(stream.ToArray());
    }

    internal static void End(BinaryWriter writer) => SlotFixture.String(writer, "None");
}
