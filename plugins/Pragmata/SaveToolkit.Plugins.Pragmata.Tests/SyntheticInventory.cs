using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;

namespace SaveToolkit.Plugins.Pragmata.Tests;

/// <summary>Test-only typed payload in a real container; never a production structural writer.</summary>
internal static class SyntheticInventory
{
    internal const uint FloatHash = 0x11223344;

    public static byte[] Create(int count)
    {
        var template = SampleEvidenceTests.Files().Single(file => file.FileName == "data000.bin").ReadAllBytes();
        var container = new DsssContainer(template);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Root(KnownHashes.DetailRoot, 1);
        Primitive(KnownHashes.Slot, FieldTags.Int32, 4, () => writer.Write(0));
        Root(KnownHashes.HeaderRoot, 4);
        Primitive(KnownHashes.SaveDate, FieldTags.Int64, 8, () => writer.Write(0L));
        Primitive(KnownHashes.Playtime, FieldTags.UInt64, 8, () => writer.Write(100UL));
        Primitive(KnownHashes.Cleared, FieldTags.Boolean, 1, () => writer.Write(false));
        Primitive(KnownHashes.Difficulty, FieldTags.UInt32, 4, () => writer.Write(0U));
        Root(KnownHashes.GameRoot, 1);
        Array(KnownHashes.GameSections, 2);
        Class(KnownHashes.InventoryClass, 2);
        Array(KnownHashes.Items, count);
        for (var index = 0; index < count; index++)
        {
            Class(0x11111111, 3);
            Primitive(KnownHashes.ItemId, FieldTags.UInt32, 4, () => writer.Write((uint)index + 1));
            Primitive(KnownHashes.Quantity, FieldTags.Int32, 4, () => writer.Write(index % 11));
            Primitive(FloatHash, FieldTags.Float32, 4, () => writer.Write(index % 2 == 0 ? -2.5f : 4.75f));
        }
        Primitive(KnownHashes.EquippedItem, FieldTags.UInt32, 4, () => writer.Write(1U));
        Class(KnownHashes.ClockClass, 3);
        Primitive(KnownHashes.Elapsed, FieldTags.UInt64, 8, () => writer.Write(100UL));
        Primitive(KnownHashes.Demo, FieldTags.UInt64, 8, () => writer.Write(0UL));
        Primitive(KnownHashes.Pause, FieldTags.UInt64, 8, () => writer.Write(0UL));
        Root(0xfafafafa, 1);
        writer.Write(0xbabababaU);
        writer.Write(FieldTags.String);
        var remaining = container.Plaintext.Length - (int)stream.Position - sizeof(int);
        Assert.True(remaining >= 0 && remaining % 2 == 0);
        writer.Write(remaining / 2);
        writer.Write(new byte[remaining]);
        var payload = stream.ToArray();
        Assert.Equal(count, new SaveLayout(payload).Items.Count);
        return container.Rebuild(payload);

        void Align(int size)
        {
            var target = ((stream.Position + 16 + size - 1) & ~(size - 1)) - 16;
            while (stream.Position < target) writer.Write((byte)0);
        }
        void Class(uint hash, int fields) { writer.Write(fields); writer.Write(hash); }
        void Root(uint hash, int fields) { writer.Write(hash); Class(hash, fields); }
        void Primitive(uint hash, int type, int size, Action value)
        {
            writer.Write(hash); writer.Write(type); Align(4); writer.Write(size); Align(size); value(); Align(4);
        }
        void Array(uint hash, int elements)
        {
            writer.Write(hash); writer.Write(FieldTags.Array); Align(4);
            writer.Write(FieldTags.Class); writer.Write(0); writer.Write(elements); writer.Write(1);
        }
    }
}
