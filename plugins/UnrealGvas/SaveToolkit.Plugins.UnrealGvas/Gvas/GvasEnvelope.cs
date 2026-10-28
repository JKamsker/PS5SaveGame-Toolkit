using System.Buffers.Binary;
using System.IO.Compression;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

/// <summary>Supports raw GVAS, RFC1950 zlib, and the documented Astroneer zlib envelope.</summary>
public sealed class GvasEnvelope
{
    private static ReadOnlySpan<byte> AstroneerMagic => [0xBE, 0x40, 0x37, 0x4A, 0xEE, 0x0B, 0x74, 0xA3, 1, 0, 0, 0];
    private readonly byte[] _original;
    private readonly int _compressedOffset;
    public GvasFile File { get; private set; }
    public string Label => _compressedOffset switch { 0 => "Plain GVAS", 16 => "Astroneer zlib GVAS", _ => "zlib GVAS" };

    private GvasEnvelope(byte[] original, int compressedOffset, GvasFile file)
    {
        _original = original;
        _compressedOffset = compressedOffset;
        File = file;
    }

    public static string? Probe(Stream stream)
    {
        try
        {
            Span<byte> prefix = stackalloc byte[16];
            var read = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
            if (read < 4) return null;
            if (prefix[..4].SequenceEqual("GVAS"u8))
            {
                if (stream.CanSeek)
                {
                    stream.Seek(-read, SeekOrigin.Current);
                    return CanOpen(stream) ? "Unreal GVAS" : null;
                }
                using var plain = new PrefixStream(prefix[..read].ToArray(), stream);
                return CanOpen(plain) ? "Unreal GVAS" : null;
            }
            if (read >= 16 && prefix[..12].SequenceEqual(AstroneerMagic))
            {
                using var zlib = new ZLibStream(stream, CompressionMode.Decompress, leaveOpen: true);
                return CanOpen(zlib) ? "Unreal GVAS (Astroneer zlib)" : null;
            }
            // Replay the consumed prefix without relying on seekable input streams.
            if ((prefix[0] & 15) != 8 || ((prefix[0] << 8) + prefix[1]) % 31 != 0) return null;
            using var replay = new PrefixStream(prefix[..read].ToArray(), stream);
            using var compressed = new ZLibStream(replay, CompressionMode.Decompress);
            return CanOpen(compressed) ? "Unreal GVAS (zlib)" : null;
        }
        catch (Exception ex) when (ex is SaveFormatException or IOException or InvalidDataException or ArgumentException or InvalidOperationException) { return null; }
    }

    private static bool CanOpen(Stream stream)
    {
        _ = GvasHeaderReader.Read(stream);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        // A root property name (possibly the None terminator) must follow the header.
        // Do not walk property tags or payloads: Probe stays independent of save-body size.
        return GvasString.Read(reader).Length != 0;
    }

    public static GvasEnvelope Open(byte[] original)
    {
        if (original.AsSpan().StartsWith("GVAS"u8)) return new(original, 0, GvasFile.Parse(original));
        var astroneer = original.AsSpan().StartsWith(AstroneerMagic);
        var offset = astroneer ? 16 : 0;
        if (original.Length < offset + 2) throw new SaveFormatException("Truncated GVAS wrapper.");
        if ((original[offset] & 15) != 8 || ((original[offset] << 8) + original[offset + 1]) % 31 != 0)
            throw new SaveFormatException("No GVAS magic or supported zlib envelope. Custom Unreal save serializers are not supported.");
        try
        {
            using var input = new MemoryStream(original, offset, original.Length - offset, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = zlib.Read(buffer)) != 0)
            {
                if (output.Length + read > GvasFile.MaxFileLength) throw new SaveFormatException("Unwrapped GVAS exceeds the 128 MiB safety limit.");
                output.Write(buffer.AsSpan(0, read));
            }
            var bytes = output.ToArray();
            ValidateZlib(original.AsSpan(offset), bytes);
            if (astroneer && BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(12)) != bytes.Length)
                throw new SaveFormatException("Astroneer uncompressed size does not match its header.");
            // Compression suffixes/dictionaries are not understood: prohibit rewriting wrapped files.
            return new(original, astroneer ? 16 : 2, GvasFile.Parse(bytes));
        }
        catch (InvalidDataException ex) { throw new SaveFormatException("Not supported GVAS, or invalid zlib wrapper.", ex); }
    }

    public bool IsWrapped => _compressedOffset != 0;

    internal static void ValidateZlib(ReadOnlySpan<byte> compressed, ReadOnlySpan<byte> plain)
    {
        const uint adlerPrime = 65521;
        uint a = 1, b = 0;
        foreach (var value in plain)
        {
            a = (a + value) % adlerPrime;
            b = (b + a) % adlerPrime;
        }
        if (compressed.Length < 6 || BinaryPrimitives.ReadUInt32BigEndian(compressed[^4..]) != (b << 16 | a))
            throw new SaveFormatException("Invalid or truncated zlib Adler-32 trailer (trailing wrapper data is not supported).");
    }

    public byte[] Serialize()
    {
        var bytes = File.Serialize();
        if (!IsWrapped) return bytes;
        if (File.HasChanges) throw new SaveFormatException("Wrapped GVAS is read-only until wrapper recompression is verified.");
        _ = Open(_original);
        return _original.ToArray();
    }

    internal void ReplaceFile(GvasFile file)
    {
        if (IsWrapped) throw new SaveFormatException("Wrapped GVAS cannot replace its uncompressed file.");
        File = file;
    }

    private sealed class PrefixStream(byte[] prefix, Stream tail) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (_position >= prefix.Length) return tail.Read(buffer);
            var count = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
