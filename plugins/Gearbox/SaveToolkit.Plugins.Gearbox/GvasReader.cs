using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>SDK reader adapter retaining the established Gearbox diagnostics.</summary>
internal ref struct GvasReader(ReadOnlySpan<byte> data)
{
    private const int MaximumStringBytes = 16_384;
    private static readonly FStringLimits StringLimits = new(MaximumStringBytes, MaximumStringBytes / sizeof(char));
    private BoundedBinaryCursor _cursor = new(data, "Gearbox GVAS header");
    public readonly int Position => _cursor.Position;

    public ReadOnlySpan<byte> Take(int count)
    {
        try { return _cursor.Take(count); }
        catch (SaveFormatException) { throw new SaveFormatException("Truncated Gearbox GVAS header."); }
    }

    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)));
    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(sizeof(ushort)));

    public string String()
    {
        var length = Int32();
        int byteLength;
        try { byteLength = FStringCodec.ByteLength(length, StringLimits, _cursor.Context); }
        catch (SaveFormatException) { throw new SaveFormatException("Invalid GVAS string length."); }
        var bytes = Take(byteLength);
        // Length and body size are already validated; Decode can now fail only on encoding or NUL termination.
        try { return FStringCodec.Decode(length, bytes, StringLimits, _cursor.Context); }
        catch (SaveFormatException ex) when (ex.InnerException is DecoderFallbackException)
        {
            throw new SaveFormatException("Invalid GVAS string encoding.", ex.InnerException);
        }
        catch (SaveFormatException) { throw new SaveFormatException("GVAS string is not terminated."); }
    }
}
