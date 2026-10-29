using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>SaveExtension limits and legacy diagnostics around the shared binary readers.</summary>
internal static class SaveExtensionBinary
{
    private const int MaxStringUnits = 1_048_576;
    private static readonly FStringLimits DefaultLimits = Limits(MaxStringUnits);
    private static readonly FStringLimits NameLimits = Limits(RecordIndex.MaxNameUnits);

    internal static BoundedBinaryCursor Cursor(ReadOnlySpan<byte> data, int start = 0, int? end = null) =>
        new(data, "SaveExtension", start, end);

    internal static ReadOnlySpan<byte> TakeBytes(this ref BoundedBinaryCursor cursor, int length)
    {
        if (length < 0 || length > cursor.Remaining)
            throw new SaveFormatException($"Truncated SaveExtension data at 0x{cursor.Position:X}.");
        return cursor.Take(length);
    }

    internal static byte Byte(this ref BoundedBinaryCursor cursor) => Cursor(cursor.TakeBytes(sizeof(byte))).ReadByte();
    internal static ushort UInt16(this ref BoundedBinaryCursor cursor) => Cursor(cursor.TakeBytes(sizeof(ushort))).ReadUInt16LE();
    internal static int Int32(this ref BoundedBinaryCursor cursor) => Cursor(cursor.TakeBytes(sizeof(int))).ReadInt32LE();
    internal static long Int64(this ref BoundedBinaryCursor cursor) => Cursor(cursor.TakeBytes(sizeof(long))).ReadInt64LE();
    internal static ulong UInt64(this ref BoundedBinaryCursor cursor) => Cursor(cursor.TakeBytes(sizeof(ulong))).ReadUInt64LE();
    internal static float Single(this ref BoundedBinaryCursor cursor) => Cursor(cursor.TakeBytes(sizeof(float))).ReadSingleLE();
    internal static double Double(this ref BoundedBinaryCursor cursor) => Cursor(cursor.TakeBytes(sizeof(double))).ReadDoubleLE();

    internal static int Count(this ref BoundedBinaryCursor cursor, int maximum)
    {
        var count = cursor.Int32();
        if (count < 0 || count > maximum) throw new SaveFormatException("Invalid SaveExtension count or length.");
        return count;
    }

    internal static string String(this ref BoundedBinaryCursor cursor, int maximumUnits = MaxStringUnits)
    {
        var length = cursor.Int32();
        if (length == 0) return string.Empty;
        if (length == int.MinValue || Math.Abs(length) > maximumUnits) throw new SaveFormatException("SaveExtension string is too long.");
        var limits = maximumUnits == MaxStringUnits ? DefaultLimits :
            maximumUnits == RecordIndex.MaxNameUnits ? NameLimits : Limits(maximumUnits);
        var bytes = cursor.TakeBytes(FStringCodec.ByteLength(length, limits, cursor.Context));
        try { return FStringCodec.Decode(length, bytes, limits, cursor.Context); }
        catch (SaveFormatException ex) when (ex.InnerException is DecoderFallbackException)
        { throw new SaveFormatException("Invalid SaveExtension string encoding.", ex.InnerException); }
        catch (SaveFormatException) { throw new SaveFormatException("Unterminated SaveExtension string."); }
    }

    private static FStringLimits Limits(int units) => new(2 * units, units) { MaxUtf8Bytes = units };
}
