using System.Text;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

internal static class GvasFixture
{
    public static byte[] Build(Action<BinaryWriter> properties, int major = 4, int minor = 27, bool suffix = true,
        string saveClass = "/Script/Test.Save", int customVersions = 1)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("GVAS"u8);
        writer.Write(major >= 5 ? 3 : 2);
        writer.Write(524);
        if (major >= 5) writer.Write(1009);
        writer.Write((ushort)major);
        writer.Write((ushort)minor);
        writer.Write((ushort)2);
        writer.Write(42U);
        String(writer, "++Test");
        writer.Write(3);
        writer.Write(customVersions);
        for (int i = 0; i < customVersions; i++)
        {
            writer.Write(Enumerable.Range(0, 16).Select(value => (byte)value).ToArray());
            writer.Write(7);
        }
        String(writer, saveClass);
        if (major >= 5 && minor >= 4) writer.Write((byte)0);
        properties(writer);
        String(writer, "None");
        if (suffix) writer.Write(new byte[] { 0xAB, 0xCD, 0xEF, 0x42 });
        return stream.ToArray();
    }

    public static byte[] Body(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return stream.ToArray();
    }

    public static void String(BinaryWriter writer, string value, bool wide = false)
    {
        var bytes = (wide ? Encoding.Unicode : Encoding.UTF8).GetBytes(value + '\0');
        writer.Write(wide ? -bytes.Length / 2 : bytes.Length);
        writer.Write(bytes);
    }

    public static void Property(BinaryWriter writer, string name, string type, byte[] body,
        string? inner = null, string? mapValue = null, bool boolean = false, bool guid = false, int index = 0)
    {
        String(writer, name);
        String(writer, type);
        writer.Write(body.Length);
        writer.Write(index);
        if (type is "ArrayProperty" or "SetProperty" or "MapProperty" or "EnumProperty" or "ByteProperty")
            String(writer, inner ?? "None");
        if (type == "MapProperty") String(writer, mapValue!);
        if (type == "StructProperty") { String(writer, inner!); writer.Write(new byte[16]); }
        if (type == "BoolProperty") writer.Write(boolean);
        writer.Write(guid);
        if (guid) writer.Write(Enumerable.Range(16, 16).Select(i => (byte)i).ToArray());
        writer.Write(body);
    }

    public static byte[] Primitives() => Build(w =>
    {
        Property(w, "Count", "IntProperty", Body(b => b.Write(12)), guid: true, index: 3);
        Property(w, "Health", "FloatProperty", Body(b => b.Write(1.25f)));
        Property(w, "Enabled", "BoolProperty", [], boolean: true);
        Property(w, "Name", "StrProperty", Body(b => String(b, "Hero")));
        Property(w, "Localized", "TextProperty", Body(b => { b.Write(0U); b.Write((sbyte)0); String(b, "ns"); String(b, "key"); String(b, "Hello"); }));
        Property(w, "Invariant", "TextProperty", Body(b => { b.Write(0U); b.Write((sbyte)-1); b.Write(1); String(b, "World"); }));
        Property(w, "Enum", "EnumProperty", Body(b => String(b, "ETest::First")), "ETest");
        Property(w, "EnumByte", "ByteProperty", Body(b => String(b, "ETest::First")), "ETest");
        Property(w, "Wide", "NameProperty", Body(b => String(b, "Wand", true)));
        Property(w, "Object", "ObjectProperty", Body(b => String(b, "/Game/Test")));
        Property(w, "Signed8", "Int8Property", [0xFF]);
        Property(w, "Signed16", "Int16Property", Body(b => b.Write((short)-123)));
        Property(w, "Signed64", "Int64Property", Body(b => b.Write(long.MinValue)));
        Property(w, "Unsigned8", "UInt8Property", [255]);
        Property(w, "Unsigned16", "UInt16Property", Body(b => b.Write(ushort.MaxValue)));
        Property(w, "Unsigned32", "UInt32Property", Body(b => b.Write(uint.MaxValue)));
        Property(w, "Unsigned64", "UInt64Property", Body(b => b.Write(ulong.MaxValue)));
        Property(w, "Double", "DoubleProperty", Body(b => b.Write(3.141592653589793)));
        Property(w, "SoftObject", "SoftObjectProperty", Body(b => { String(b, "/Game/Asset"); String(b, "Subpath"); }));
        Property(w, "Unknown", "FutureProperty", [0xFE, 0xED, 0xFA, 0xCE]);
    });

    public static byte[] Nested() => Build(w =>
    {
        Property(w, "Root", "StructProperty", Body(root =>
        {
            Property(root, "Items", "ArrayProperty", Body(array =>
            {
                array.Write(2);
                var elements = Body(items =>
                {
                    for (var i = 0; i < 2; i++)
                    {
                        Property(items, "Caption", "StrProperty", Body(b => String(b, $"Item{i}")));
                        Property(items, "Quantity", "IntProperty", Body(b => b.Write(i + 1)));
                        String(items, "None");
                    }
                });
                Property(array, "Items", "StructProperty", elements, "InventoryItem");
            }), "StructProperty");
            Property(root, "Map", "MapProperty", Body(map =>
            {
                map.Write(0); map.Write(2);
                String(map, "A"); map.Write(10);
                String(map, "B"); map.Write(20);
            }), "StrProperty", "IntProperty");
            Property(root, "Set", "SetProperty", Body(set =>
            {
                set.Write(0); set.Write(2); String(set, "One"); String(set, "Two");
            }), "StrProperty");
            Property(root, "Bools", "ArrayProperty", Body(b => { b.Write(2); b.Write(true); b.Write(false); }), "BoolProperty");
            String(root, "None");
        }), "Inventory");
    });
}
