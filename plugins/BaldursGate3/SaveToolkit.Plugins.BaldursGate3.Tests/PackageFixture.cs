using System.IO.Compression;
using System.Text;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

internal static class PackageFixture
{
    // Independently encoded wire layouts from LSLib's PackageFormat.cs.
    internal static byte[] Create(uint version = 18, byte method = 0, params (string Name, byte[] Bytes)[] payloads)
    {
        var headerSize = version == 15 ? 38 : 40;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(new byte[headerSize]);
        using var table = new MemoryStream();
        using var rows = new BinaryWriter(table, Encoding.UTF8, true);
        foreach (var (name, bytes) in payloads)
        {
            var compressed = Compress(bytes, method);
            var offset = output.Position;
            writer.Write(compressed);
            var encodedName = new byte[256];
            Encoding.UTF8.GetBytes(name).CopyTo(encodedName, 0);
            rows.Write(encodedName);
            if (version == 18)
            {
                rows.Write((uint)offset);
                rows.Write((ushort)0);
                rows.Write((byte)0); // part
                rows.Write(method);
                rows.Write((uint)compressed.Length);
                rows.Write(method == 0 ? 0u : (uint)bytes.Length);
            }
            else
            {
                rows.Write((ulong)offset);
                rows.Write((ulong)compressed.Length);
                rows.Write(method == 0 ? 0UL : (ulong)bytes.Length);
                rows.Write(0u); // part
                rows.Write((uint)method);
                rows.Write(0u); // legacy CRC; reader does not authenticate this
                rows.Write(0u); // reserved
            }
        }

        var listOffset = output.Position;
        var list = Compress(table.ToArray(), 2);
        writer.Write(payloads.Length);
        writer.Write(list.Length);
        writer.Write(list);
        var listSize = output.Position - listOffset;
        output.Position = 0;
        writer.Write("LSPK"u8);
        writer.Write(version);
        writer.Write((ulong)listOffset);
        writer.Write((uint)listSize);
        writer.Write((byte)0); // package flags
        writer.Write((byte)0); // priority
        // Independent MD5 vector in physical write order, per LSLib; table order may differ.
#pragma warning disable CA5351
        var digest = System.Security.Cryptography.MD5.HashData(payloads.SelectMany(p => p.Bytes).ToArray());
#pragma warning restore CA5351
        writer.Write(digest.Select(b => unchecked((byte)(b + 1))).ToArray());
        if (version != 15)
        {
            writer.Write((ushort)1);
        }

        return output.ToArray();
    }

    internal static byte[] Compress(byte[] bytes, byte method, bool framed = false)
    {
        using var output = new MemoryStream();
        if (method == 0)
        {
            return bytes;
        }

        if (method == 2 && !framed)
        {
            var buffer = new byte[LZ4Codec.MaximumOutputSize(bytes.Length)];
            var size = LZ4Codec.Encode(bytes, buffer);
            return buffer[..size];
        }

        using (Stream encoder = method switch
        {
            1 => new ZLibStream(output, CompressionLevel.Optimal, true),
            2 => LZ4Stream.Encode(output, leaveOpen: true),
            3 => new ZstdSharp.CompressionStream(output, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        })
        {
            encoder.Write(bytes);
        }

        return output.ToArray();
    }
}
