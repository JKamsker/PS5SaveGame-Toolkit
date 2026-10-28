using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.EldenRing.Tests;

// Independently constructed, minimal allocation. No sample bytes are embedded.
internal sealed class SyntheticSave
{
    public const int HeaderSize = 0x70;
    public const int SlotSize = 0x280000;
    public const int SlotCount = 10;
    public const int FileSize = 0x1BA0080;
    public const int UserData = 0x1900070;
    public const int Menu = UserData + 0x14C;
    public const int ProfileSize = 0x24C;
    public const int Attributes = 0x34;
    public const int Level = 0x60;
    public const int Runes = 0x64;
    public const int RuneMemory = 0x68;
    public const int Name = 0x94;
    public const int ProfileLevel = 0x22;
    public const int ProfileSeconds = 0x26;
    public const int ProfileMemory = 0x2A;
    public const uint InitialRunes = 100;
    public const uint LifetimeRunes = 1000;
    public const int AttributeCount = 8;
    public const int NameBytes = 34;
    public const ulong PlayedMilliseconds = 123_456;
    public const int MillisecondsPerSecond = 1000;

    public SyntheticSave(IReadOnlyList<int> slots, uint version = 150, int menuSize = 0x1800, int trailingBytes = 0)
    {
        Bytes = new byte[FileSize + trailingBytes];
        Write32(0, 0x2C9C01CB);
        Write32(8, 0x7F7F7F7F);
        Write32(UserData, 90);
        Write32(Menu + sizeof(uint), (uint)menuSize);
        ActiveOffset = Menu + 8 + menuSize;
        foreach (var index in slots)
        {
            CreateCharacter(index, version);
        }
    }

    public byte[] Bytes { get; }
    public int ActiveOffset { get; }
    public Dictionary<int, int> Players { get; } = [];
    public int Profile(int index) => ActiveOffset + SlotCount + index * ProfileSize;
    public static int Slot(int index) => HeaderSize + index * SlotSize;
    public ISaveFile File(string title = "PPSA03169") => MemorySaveSet.SingleFile("memory.dat", Bytes, title, "sce_sdmemory");
    public ISaveDocument Open() => new EldenRingPlugin().Open(File());

    public void Write32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(offset), value);
    public void Write64(int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Bytes.AsSpan(offset), value);

    public void Rename(int index, string name)
    {
        var encoded = Encoding.Unicode.GetBytes(name);
        foreach (var offset in new[] { Players[index] + Name, Profile(index) })
        {
            Bytes.AsSpan(offset, NameBytes).Clear();
            encoded.CopyTo(Bytes, offset);
        }
    }

    private void CreateCharacter(int index, uint version)
    {
        var start = Slot(index);
        Write32(start, version);
        Write64(start + 8, PlayedMilliseconds);
        Bytes[ActiveOffset + index] = 1;
        var cursor = start + 0x20;
        var itemCount = version <= 81 ? 5118 : 5120;
        for (var item = 0; item < itemCount; item++)
        {
            // Empty, weapon, armour and other entry sizes all participate in locating the player.
            var id = item switch { 1 => 0x00001000U, 2 => 0x10001000U, 3 => 0x40001000U, _ => 0U };
            Write32(cursor + sizeof(uint), id);
            cursor += 8;
            cursor += item switch { 1 => 13, 2 => 8, _ => 0 };
        }

        Players.Add(index, cursor);
        for (var attribute = 0; attribute < AttributeCount; attribute++)
        {
            Write32(cursor + Attributes + attribute * sizeof(uint), 10);
        }

        Write32(cursor + Level, 1);
        Write32(cursor + Runes, InitialRunes);
        Write32(cursor + RuneMemory, LifetimeRunes);
        Write32(Profile(index) + ProfileLevel, 1);
        Write32(Profile(index) + ProfileSeconds, (uint)(PlayedMilliseconds / MillisecondsPerSecond));
        Write32(Profile(index) + ProfileMemory, LifetimeRunes);
        Rename(index, $"Hero {index}");
    }
}
