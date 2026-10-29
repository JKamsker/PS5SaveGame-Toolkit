using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record PackageEntry(string Name, int Offset, int StoredSize, int Size, byte Flags)
{
    internal LarianCompressionMethod Compression => LarianCompression.Method(Flags);
}

/// <summary>Managed, bounded reader of the LSLib v15/v16/v18 package layouts.</summary>
internal sealed class LspkPackage
{
    private readonly byte[] _bytes;

    private LspkPackage(byte[] bytes, uint version, byte flags, List<PackageEntry> entries)
    {
        _bytes = bytes;
        Version = version;
        Flags = flags;
        Entries = entries;
    }

    internal uint Version { get; }
    internal byte Flags { get; }
    internal IReadOnlyList<PackageEntry> Entries { get; }
    internal string HeaderDigest => Convert.ToHexString(_bytes.AsSpan(LspkLayout.Digest, LspkLayout.DigestSize));

    internal static LspkPackage Read(byte[] bytes) => ReadCore(bytes, null);

    // Transfer the validation buffers to callers that need every payload. The package
    // does not retain them, and ordinary Extract calls still return independent arrays.
    internal static LspkPackage Read(byte[] bytes, out Dictionary<string, byte[]> payloads)
    {
        payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        return ReadCore(bytes, payloads);
    }

    private static LspkPackage ReadCore(byte[] bytes, Dictionary<string, byte[]>? payloads)
    {
        BinaryData.Require(bytes.Length >= LspkLayout.Header15Size && bytes.AsSpan().StartsWith("LSPK"u8), "Not an LSPK package.");
        var version = BinaryData.U32(bytes, LspkLayout.Version);
        BinaryData.Require(version is 15 or 16 or 18, $"Unsupported LSPK version {version}; expected 15, 16 or 18.");
        var headerSize = version == 15 ? LspkLayout.Header15Size : LspkLayout.Header16Size;
        BinaryData.Slice(bytes, 0, headerSize);
        BinaryData.Require(version == 15 || BinaryData.U16(bytes, LspkLayout.PartCount) == 1, "Multipart LSPK packages are not supported.");
        BinaryData.Require((bytes[LspkLayout.Flags] & LspkLayout.SolidFlag) == 0, "Solid LSPK packages are not supported; this reader requires independent entries.");
        var listOffset = BinaryData.Size(BinaryData.U64(bytes, LspkLayout.FileListOffset));
        var listSize = BinaryData.Size(BinaryData.U32(bytes, LspkLayout.FileListSize));
        BinaryData.Require(listOffset >= headerSize && listSize >= LspkLayout.FileListPreambleSize, "Invalid LSPK file list location.");
        var list = BinaryData.Slice(bytes, listOffset, listSize);
        var count = BinaryData.I32(list, 0);
        BinaryData.Require(count is >= 0 and <= BinaryData.MaxRecords, "Invalid LSPK entry count.");
        var compressedSize = BinaryData.Size(BinaryData.U32(list, LspkLayout.FileListCompressedSize));
        BinaryData.Require(compressedSize == listSize - LspkLayout.FileListPreambleSize, "Invalid LSPK compressed file list size.");
        var entrySize = version == 18 ? LspkLayout.Entry18Size : LspkLayout.Entry15Size;
        var table = LarianCompression.Decode(list[LspkLayout.FileListPreambleSize..],
            BinaryData.Size((ulong)count * (ulong)entrySize), (byte)LarianCompressionMethod.Lz4);
        var entries = new List<PackageEntry>(count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        long totalSize = 0;
        for (var i = 0; i < count; i++)
        {
            var row = table.AsSpan(i * entrySize, entrySize);
            var terminator = row[..LspkLayout.FileNameSize].IndexOf((byte)0);
            BinaryData.Require(terminator > 0, "LSPK entry name is empty or not null-terminated.");
            var name = BinaryData.Utf8.GetString(row[..terminator]);
            BinaryData.Require(names.Add(name), "Duplicate LSPK entry name.");
            var entry = ReadEntry(row, name, version);
            BinaryData.Require(entry.Offset >= headerSize && entry.Offset <= listOffset && entry.StoredSize <= listOffset - entry.Offset,
                $"LSPK entry '{name}' points outside the payload area.");
            totalSize += entry.Size;
            BinaryData.Require(totalSize <= BinaryData.MaxBufferSize, "LSPK total expanded size exceeds 256 MiB.");
            entries.Add(entry);
        }

        var previousEnd = headerSize;
        foreach (var entry in entries.OrderBy(e => e.Offset))
        {
            BinaryData.Require(entry.Offset >= previousEnd, "LSPK payload entries overlap.");
            previousEnd = entry.Offset + entry.StoredSize;
        }

        var package = new LspkPackage(bytes, version, bytes[LspkLayout.Flags], entries);
        if (version == 18)
        {
            LspkIntegrity.Validate(package, payloads);
        }
        else if (payloads is not null)
            foreach (var entry in entries) payloads.Add(entry.Name, package.Extract(entry));

        return package;
    }

    internal byte[] Extract(PackageEntry entry) =>
        LarianCompression.Decode(_bytes.AsSpan(entry.Offset, entry.StoredSize), entry.Size, entry.Flags);

    private static PackageEntry ReadEntry(ReadOnlySpan<byte> row, string name, uint version)
    {
        var offset = version == 18 ? BinaryData.U32(row, LspkLayout.EntryOffset) |
            ((ulong)BinaryData.U16(row, LspkLayout.Entry18OffsetHigh) << LspkLayout.OffsetHighShift) : BinaryData.U64(row, LspkLayout.EntryOffset);
        var part = version == 18 ? row[LspkLayout.Entry18Part] : BinaryData.U32(row, LspkLayout.Entry15Part);
        var flags = version == 18 ? row[LspkLayout.Entry18Flags] : BinaryData.U32(row, LspkLayout.Entry15Flags);
        BinaryData.Require(part == 0, "LSPK entry refers to an unsupported external archive part.");
        BinaryData.Require(flags <= byte.MaxValue, "Invalid LSPK compression flags.");
        var method = LarianCompression.Method((byte)flags);
        var stored = BinaryData.Size(version == 18 ? BinaryData.U32(row, LspkLayout.EntryStoredSize) : BinaryData.U64(row, LspkLayout.EntryStoredSize));
        var expanded = method == LarianCompressionMethod.None ? stored :
            BinaryData.Size(version == 18 ? BinaryData.U32(row, LspkLayout.Entry18ExpandedSize) : BinaryData.U64(row, LspkLayout.Entry15ExpandedSize));
        return new PackageEntry(name, BinaryData.Size(offset), stored, expanded, (byte)flags);
    }
}
