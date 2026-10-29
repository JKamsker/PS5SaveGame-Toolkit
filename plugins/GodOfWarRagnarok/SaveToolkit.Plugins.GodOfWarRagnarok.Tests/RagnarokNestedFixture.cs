using System.Buffers.Binary;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

/// <summary>Independent in-memory framing with resource, ID, index, link and flag rows.</summary>
internal sealed class RagnarokNestedFixture(int walletCount = 3, int groupCount = 2)
{
    public const int FirstWalletOffset = 0x500;
    public const int WalletSize = 20 + 16 + 8 + 4;
    public const int FlagPrefixSize = 16;
    public const int FlagGroupSize = 12 + 12;
    public const int LinkCount = 1;
    public const ushort LinksLength = 4 + 22 * LinkCount;
    public int WalletBlockEnd => FirstWalletOffset + walletCount * WalletSize;
    public int FlagsLengthOffset => WalletBlockEnd + sizeof(ushort) + LinksLength;
    public int FlagsPayloadOffset => FlagsLengthOffset + sizeof(ushort);
    public int FlagsEnd => FlagsPayloadOffset + FlagPrefixSize + groupCount * FlagGroupSize;
    public int WalletOffset(int row) => FirstWalletOffset + row * WalletSize;
    public int GroupOffset(int row) => FlagsPayloadOffset + FlagPrefixSize + row * FlagGroupSize;

    public byte[] Create()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(RagnarokTestSupport.LegacyGameplay().AsSpan(0, FirstWalletOffset));
        for (var wallet = 0; wallet < walletCount; wallet++)
        {
            writer.Write(wallet == 0 ? RagnarokFormat.HeroWalletId : (ulong)wallet);
            writer.Write(1u); // Resource rows.
            writer.Write(1u); // Subsequent IDs.
            writer.Write(1u); // Index-like words; no relationship to IDs is asserted.
            writer.Write(0x5AE80674D240529Eul);
            writer.Write(1234u);
            writer.Write(7u);
            writer.Write(0xABCDEFul);
            writer.Write(uint.MaxValue);
        }

        writer.Write(LinksLength);
        writer.Write((uint)LinkCount);
        writer.Write(new byte[22 * LinkCount]);
        writer.Write(checked((ushort)(FlagPrefixSize + groupCount * FlagGroupSize)));
        writer.Write(ulong.MaxValue);
        writer.Write(1u);
        writer.Write((uint)groupCount);
        for (var group = 0; group < groupCount; group++)
        {
            writer.Write(0x100ul + (ulong)group);
            writer.Write(1u);
            writer.Write(0x123ul);
            writer.Write(0x04030201u);
        }

        writer.Write(uint.MaxValue);
        var bytes = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(FirstWalletOffset - sizeof(uint)),
            (uint)(walletCount * WalletSize));
        return bytes;
    }
}
