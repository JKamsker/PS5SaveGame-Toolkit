using System.Text;

namespace SaveToolkit.Plugins.Witcher3.Tests;

/// <summary>Independent synthetic layout builder; contains no sample bytes or production writer calls.</summary>
internal sealed class SyntheticPlayerSave : IDisposable
{
    private readonly MemoryStream _stream = new();
    private readonly BinaryWriter _writer;
    private readonly List<string> _names = [];
    private readonly List<(int Offset, int Size)> _entries = [];

    private SyntheticPlayerSave() => _writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);

    public static byte[] Create(bool storedPoints = false)
    {
        using var fixture = new SyntheticPlayerSave();
        return fixture.Build(storedPoints);
    }

    public void Dispose() { _writer.Dispose(); _stream.Dispose(); }

    private int Position => checked((int)_stream.Position);
    private void Magic(string text) => _writer.Write(Encoding.ASCII.GetBytes(text));
    private ushort Name(string name)
    {
        var index = _names.IndexOf(name);
        if (index < 0)
        {
            _names.Add(name);
            index = _names.Count - 1;
        }

        return checked((ushort)(index + 1));
    }

    private byte[] Build(bool storedPoints)
    {
        Magic("SAV3");
        Version();
        Block("universe", () => {
            Block("Player", () => Variable("id", "IdTag", PlayerId()));
            Block("world", () => Block("layerStorage", () => Block("entityData", () => {
                Variable("idTag", "IdTag", PlayerId());
                Archive(storedPoints);
            })));
        });
        var rb = Position;
        Magic("RB");
        _writer.Write(1);
        _writer.Write(Name("universe"));
        _writer.Write(16 + SyntheticSave.HeaderSize);
        var nm = Position;
        Magic("NMMANU");
        _writer.Write(_names.Count);
        _writer.Write(0x12345678);
        foreach (var name in _names)
        {
            _writer.Write(checked((byte)name.Length));
            Magic(name);
        }

        _writer.Write(0x13572468);
        Magic("ENOD");
        _entries.Add((nm + 2, Position - nm - 2));
        _writer.Write(nm + SyntheticSave.HeaderSize);
        _writer.Write(rb + SyntheticSave.HeaderSize);
        Magic("SC");
        var table = Position;
        _writer.Write(_entries.Count);
        // Deliberately use reverse physical order to catch accidental directory sorting on write.
        foreach (var (offset, size) in _entries.OrderByDescending(e => e.Offset))
        {
            _writer.Write(offset + SyntheticSave.HeaderSize);
            _writer.Write(size);
        }

        _writer.Write(table + SyntheticSave.HeaderSize);
        Magic("SE");
        return SyntheticSave.Wrap(_stream.ToArray());
    }

    private void Block(string name, Action children)
    {
        var start = Position;
        Magic("BS");
        _writer.Write(Name(name));
        children();
        _entries.Add((start, Position - start));
    }

    private void Variable(string name, string type, byte[] value)
    {
        var start = Position;
        Magic("VL");
        _writer.Write(Name(name));
        _writer.Write(Name(type));
        _writer.Write(value);
        _entries.Add((start, Position - start));
    }

    private void Archive(bool storedPoints)
    {
        var start = Position;
        Magic("SS");
        var sizeAt = Position;
        _writer.Write(0);
        Magic("SXAP");
        Version();
        Magic("BLCK");
        _writer.Write(Name("Entity"));
        var blockSize = Position;
        _writer.Write(0);
        Magic("PORP");
        _writer.Write(Name("levelManager"));
        _writer.Write(Name("handle:W3LevelManager"));
        var managerSize = Position;
        _writer.Write(0);
        var handleStart = Position;
        _writer.Write(new byte[] { 0, 1 });
        _writer.Write(7);
        _writer.Write(Name("W3LevelManager"));
        _writer.Write((byte)0);
        Property("unknownProperty", "UnknownType", () => _writer.Write(new byte[] { 0x71, 0xFF, 0, 0xA2 }));
        Property("levelDefinitions", "array:2,0,SLevelDefinition", () => {
            _writer.Write(2);
            Structure(() => Int("number", 1));
            Structure(() => Int("number", 50));
        });
        Int("level", 1);
        Property("points", "array:2,0,SSpendablePoints", () => {
            _writer.Write(2);
            Structure(() => { if (storedPoints) { Int("free", 3); Int("used", 2); } });
            Structure(() => { if (storedPoints) { Int("free", 10); Int("used", 1000); } });
        });
        _writer.Write((ushort)0);
        Put(managerSize, Position - handleStart);
        Put(blockSize, Position - blockSize - 4);
        _writer.Write(new byte[] { 0, 0, 1, 0xA2, 0x15, 0x35, 0xB6 });
        _writer.Write(Name("CInventoryComponent"));
        _writer.Write((ushort)2);
        _writer.Write((ushort)2);
        Item("Crowns", 250, 1);
        Item("Bread", 5, 2);
        _writer.Write(new byte[] { 1, 0, 0, 0, 0, 0 });
        Put(sizeAt, Position - sizeAt - 4);
        _entries.Add((start, Position - start));
    }

    private void Property(string name, string type, Action value)
    {
        _writer.Write(Name(name));
        _writer.Write(Name(type));
        var sizeAt = Position;
        _writer.Write(0);
        value();
        Put(sizeAt, Position - sizeAt);
    }

    private void Int(string name, int value) => Property(name, "Int32", () => _writer.Write(value));
    private void Structure(Action properties)
    {
        _writer.Write((byte)0);
        properties();
        _writer.Write((ushort)0);
    }

    private void Item(string name, ushort quantity, byte id)
    {
        _writer.Write(Name(name));
        _writer.Write(new byte[15]);
        _writer.Write(quantity);
        _writer.Write(-1.0f);
        _writer.Write((byte)0); // Attributes.
        _writer.Write(new byte[] { id, 0, 0, 0 }); // ID and tag count.
    }

    private void Put(int offset, int value)
    {
        var end = _stream.Position;
        _stream.Position = offset;
        _writer.Write(value);
        _stream.Position = end;
    }

    private void Version() { _writer.Write(64); _writer.Write(23); _writer.Write(163); }
    private static byte[] PlayerId() => [1, .. Enumerable.Range(1, 16).Select(i => (byte)i)];
}
