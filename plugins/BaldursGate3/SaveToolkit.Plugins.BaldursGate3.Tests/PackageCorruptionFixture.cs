using System.Buffers.Binary;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

internal static class PackageCorruptionFixture
{
    // Rebuild only a synthetic v18 one-entry file table, retaining its original payload digest.
    internal static byte[] Rewrite(byte[] original, Action<byte[]> editTable, int trimPayloadBytes = 0)
    {
        var offset = (int)BinaryData.U64(original, LspkLayout.FileListOffset);
        var table = LarianCompression.Decode(original.AsSpan(offset + LspkLayout.FileListPreambleSize),
            LspkLayout.Entry18Size, 2);
        if (trimPayloadBytes > 0)
        {
            var stored = BinaryData.U32(table, LspkLayout.EntryStoredSize);
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(LspkLayout.EntryStoredSize), stored - (uint)trimPayloadBytes);
        }
        editTable(table);
        var compressed = PackageFixture.Compress(table, 2);
        var listOffset = offset - trimPayloadBytes;
        var output = new byte[listOffset + LspkLayout.FileListPreambleSize + compressed.Length];
        original.AsSpan(0, listOffset).CopyTo(output);
        BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(LspkLayout.FileListOffset), (ulong)listOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(LspkLayout.FileListSize), (uint)(compressed.Length + LspkLayout.FileListPreambleSize));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(listOffset), 1);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(listOffset + sizeof(int)), compressed.Length);
        compressed.CopyTo(output.AsSpan(listOffset + LspkLayout.FileListPreambleSize));
        return output;
    }
}
