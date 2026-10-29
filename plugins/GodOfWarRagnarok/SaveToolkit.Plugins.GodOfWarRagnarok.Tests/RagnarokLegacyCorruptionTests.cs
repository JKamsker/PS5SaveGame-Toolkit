using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokLegacyCorruptionTests
{
    private const int WalletOffset = 0x500;
    private const int IdCountOffset = WalletOffset + 12;
    private const int IndexCountOffset = WalletOffset + 16;
    private const int RowsEnd = WalletOffset + RagnarokFormat.WalletHeaderSize + RagnarokFormat.WalletRowSize;

    [Theory]
    [InlineData(IdCountOffset, uint.MaxValue)]
    [InlineData(IndexCountOffset, uint.MaxValue)]
    [InlineData(IdCountOffset, RagnarokFormat.MaximumRecordCount + 1)]
    [InlineData(IndexCountOffset, RagnarokFormat.MaximumRecordCount + 1)]
    public void LegacyAuxiliaryCountsMustRespectTheRecordBound(int offset, uint count)
    {
        var bytes = RagnarokFormatTests.SyntheticGameplay();
        Write(bytes, offset, count);
        AssertRejected(bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyAuxiliaryArrayCannotConsumeSentinel(bool indices)
    {
        var bytes = RagnarokFormatTests.SyntheticGameplay()[..(RowsEnd + (indices ? sizeof(uint) : sizeof(ulong)))];
        Write(bytes, bytes.Length - sizeof(uint), uint.MaxValue);
        // The declared array would fit only by treating the EOF sentinel as array data.
        Write(bytes, indices ? IndexCountOffset : IdCountOffset, 1);
        AssertRejected(bytes);
    }

    [Fact]
    public void LegacyAuxiliaryArraysMustFitTogether()
    {
        var bytes = RagnarokFormatTests.SyntheticGameplay()[..(RowsEnd + sizeof(ulong) + sizeof(uint))];
        Write(bytes, bytes.Length - sizeof(uint), uint.MaxValue);
        // Both counts fit independently, but together need twelve payload bytes, not eight.
        Write(bytes, IdCountOffset, 1);
        Write(bytes, IndexCountOffset, 1);
        AssertRejected(bytes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void LegacyAuxiliaryArraysEndingBeforeSentinelRemainLossless(int ids, int indices)
    {
        var end = RowsEnd + ids * sizeof(ulong) + indices * sizeof(uint);
        var bytes = RagnarokFormatTests.SyntheticGameplay()[..(end + sizeof(uint))];
        bytes.AsSpan(RowsEnd).Fill(0xA5);
        Write(bytes, IdCountOffset, (uint)ids);
        Write(bytes, IndexCountOffset, (uint)indices);
        Write(bytes, end, uint.MaxValue);
        using var document = new GodOfWarRagnarokPlugin().Open(File(bytes));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static void AssertRejected(byte[] bytes)
    {
        var error = Assert.Throws<SaveFormatException>(() => new GodOfWarRagnarokPlugin().Open(File(bytes)));
        Assert.Contains("Nested Ragnarök", error.Message);
    }

    private static SaveToolkit.Sdk.Saves.ISaveFile File(byte[] bytes) =>
        MemorySaveSet.SingleFile("GOWRSAVE0", bytes, "PPSA08330");

    private static void Write(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
