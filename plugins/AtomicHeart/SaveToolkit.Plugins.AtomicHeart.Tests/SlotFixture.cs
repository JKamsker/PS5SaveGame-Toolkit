using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Codecs;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

internal static class SlotFixture
{
    internal static byte[] Create(bool includeMetadata = true, int chunks = 1, byte[]? plain = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(1); writer.Write(2); writer.Write(522);
        writer.Write((ushort)4); writer.Write((ushort)27); writer.Write((ushort)2); writer.Write(0);
        String(writer, "++UE4+Release-4.27");
        writer.Write(3); writer.Write(0);
        String(writer, "/Script/SaveExtension.SlotInfo");
        using var info = new MemoryStream();
        using (var metadata = new BinaryWriter(info, Encoding.UTF8, leaveOpen: true))
        {
            if (includeMetadata)
            {
                String(metadata, "Version"); String(metadata, "IntProperty");
                metadata.Write(4); metadata.Write(0); metadata.Write((byte)0); metadata.Write(2);
            }
            String(metadata, "None"); metadata.Write(0);
        }
        writer.Write((int)info.Length); writer.Write(info.ToArray());
        String(writer, "/Script/SaveExtension.SlotData"); String(writer, "Oodle");
        plain ??= PlainFixture.Create();
        var compressed = OodleCodec.EncodeStored(plain);
        writer.Write((SlotDataDecoder.FrameHeaderBytes + compressed.Length) * chunks);
        for (var i = 0; i < chunks; i++)
        {
            writer.Write(0x9E2A83C1UL); writer.Write(131072UL); writer.Write((ulong)compressed.Length); writer.Write((ulong)plain.Length);
            writer.Write((ulong)compressed.Length); writer.Write((ulong)plain.Length); writer.Write(compressed);
        }
        writer.Flush();
        var bytes = stream.ToArray();
        return [.. bytes, .. Digest(bytes)];
    }

    internal static byte[] Digest(ReadOnlySpan<byte> bytes)
    {
#pragma warning disable CA5350 // Matches the game's SHA-1 trailer; no security use.
        return SHA1.HashData(bytes);
#pragma warning restore CA5350
    }

    internal static void RefreshDigest(byte[] bytes) => Digest(bytes.AsSpan(0, bytes.Length - 20)).CopyTo(bytes, bytes.Length - 20);

    internal static void String(BinaryWriter writer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + '\0');
        writer.Write(bytes.Length); writer.Write(bytes);
    }
}
