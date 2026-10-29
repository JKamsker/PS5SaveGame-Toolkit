using System.Buffers.Binary;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

/// <summary>
/// Generated fixtures and in-memory sample mutations; private bytes are never embedded. Encryption follows a fixed block
/// schedule rather than pointers in the mutated plaintext, so malformed files bypass Encode's guards.
/// Independent cipher vectors and real-sample digests remain the crypto/schema oracles.
/// </summary>
internal sealed class AriseTestContainer
{
    public const int DirectoryOffset = 0x60;
    public const int DirectoryPointerOffset = 0x38;
    public const int SectionVersionOffset = 0;
    public const int SectionIdOffset = 4;
    public const int SectionSizeOffset = 8;
    public const int NextSectionOffset = 12;
    public const int FirstSectionPointerOffset = DirectoryOffset + 8;
    public const int ItemRecordSize = 16;
    public const string FileName = "SaveData_000.sav";
    public const string TitleId = "PPSA02745";
    private const int OffsetTableOffset = 0x20;
    private const int TableSize = 0x40;
    private const int FirstSectionOffset = DirectoryOffset + TableSize;
    private readonly List<(int Offset, int Size)> _blocks = [(OffsetTableOffset, TableSize), (DirectoryOffset, TableSize)];
    private readonly byte[]? _original;

    public byte[] Payload { get; } = new byte[AriseFormat.FileSize - AriseFormat.PayloadOffset];
    public IReadOnlyList<AriseSection> Sections { get; }

    public AriseTestContainer(bool reversePhysicalOrder = false)
    {
        var schemas = AriseSchema.Sections;
        var offsets = new Dictionary<uint, int>();
        var position = FirstSectionOffset;
        foreach (var schema in reversePhysicalOrder ? schemas.Reverse() : schemas)
        {
            offsets.Add(schema.Id, position);
            position += AriseFormat.HeaderSize + schema.Size;
        }

        Write(0, 0x10051630);
        Write(4, (uint)Payload.Length);
        Payload[0x1C] = 3;
        Payload[0x1D] = 1;
        Write(DirectoryPointerOffset, DirectoryOffset);
        Write(DirectoryOffset, 100);
        Write(DirectoryOffset + 4, (uint)schemas.Count);
        Write(DirectoryOffset + 8, (uint)offsets[schemas[0].Id]);

        var sections = new List<AriseSection>();
        for (var index = 0; index < schemas.Count; index++)
        {
            var schema = schemas[index];
            var header = offsets[schema.Id];
            Write(header + SectionVersionOffset, schema.Version);
            Write(header + SectionIdOffset, schema.Id);
            Write(header + SectionSizeOffset, (uint)schema.Size);
            Write(header + NextSectionOffset, index + 1 < schemas.Count ? (uint)offsets[schemas[index + 1].Id] : 0);
            var body = header + AriseFormat.HeaderSize;
            // Nonzero unknown bytes make preservation assertions meaningful.
            Payload.AsSpan(body, schema.Size).Fill((byte)(index + 1));
            _blocks.Add((header, AriseFormat.HeaderSize));
            _blocks.Add((body, schema.Size));
            sections.Add(new(schema.Name, schema.Id, schema.Version, body, schema.Size));
        }

        Sections = sections;
        InitializeEditableFields();
    }

    public AriseTestContainer(AriseSave save)
    {
        Payload = save.Payload.ToArray();
        Sections = save.Sections;
        _original = save.Original;
        // Capture the validated schedule before the test mutates any pointers or sizes.
        _blocks[1] = (checked((int)AriseFormat.U32(Payload, DirectoryPointerOffset)), TableSize);
        foreach (var section in Sections)
        {
            _blocks.Add((section.Offset - AriseFormat.HeaderSize, AriseFormat.HeaderSize));
            _blocks.Add((section.Offset, section.Size));
        }
    }

    public int Body(string name) => Sections.Single(section => section.Name == name).Offset;
    public int Header(int index) => Sections[index].Offset - AriseFormat.HeaderSize;
    public void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Payload.AsSpan(offset), value);

    public byte[] Encrypt() => Encrypt(_blocks.Select(block => (block.Offset, block.Offset, block.Size)));

    // Stop after placing a valid header at a deliberately invalid body location. Later blocks
    // must not overwrite that header, and the decoder must reject before reading its body.
    public byte[] EncryptThroughHeader(int index, int destination) => Encrypt(_blocks.Take(2 + 2 * index)
        .Select(block => (block.Offset, block.Offset, block.Size))
        .Append((Header(index), destination, AriseFormat.HeaderSize)));

    private byte[] Encrypt(IEnumerable<(int Source, int Destination, int Size)> blocks)
    {
        var bytes = _original?.ToArray() ?? new byte[AriseFormat.FileSize];
        if (_original is null)
        {
            bytes.AsSpan(0, AriseFormat.PayloadOffset).Fill(0xA5);
        }

        Payload.CopyTo(bytes, AriseFormat.PayloadOffset);
        using var cipher = new AriseCipher();
        foreach (var (source, destination, size) in blocks)
        {
            cipher.Transform(Payload.AsSpan(source, size), bytes.AsSpan(AriseFormat.PayloadOffset + destination, size), encrypt: true);
        }

        AriseSampleTests.Hash(bytes.AsSpan(AriseFormat.CipherOffset)).CopyTo(bytes, AriseFormat.HashOffset);
        return bytes;
    }

    private void InitializeEditableFields()
    {
        var party = Body("PartyProfile");
        Write(party + 0x18, 123);
        Write(party + 0xAF8, 10);
        Write(party + 0x2000, 600);
        foreach (var section in Sections.Where(section => section.Name.StartsWith("ArisePCStatus_", StringComparison.Ordinal)))
        {
            Write(section.Offset + 4, 5);
            Write(section.Offset + 8, 321);
            Write(section.Offset + 0x30, 10);
        }

        var inventory = Body("SaveData_ItemManager");
        Payload.AsSpan(inventory, Sections.Single(section => section.Offset == inventory).Size).Clear();
        Item(0, 1, 1);
        Item(1, 2, 15);
        Item(2, 3, 16);
        Item(3, ushort.MaxValue, 5);
        Item(4, 0, 7);

        void Item(int index, ushort id, byte quantity)
        {
            var offset = inventory + index * ItemRecordSize;
            Payload[offset] = quantity;
            Payload[offset + 1] = 0x80;
            BinaryPrimitives.WriteUInt16LittleEndian(Payload.AsSpan(offset + 2), id);
        }
    }
}
