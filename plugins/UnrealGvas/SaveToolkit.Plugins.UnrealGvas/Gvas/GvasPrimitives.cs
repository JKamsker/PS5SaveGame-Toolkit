using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

internal static class GvasPrimitives
{
    public static GvasScalar Read(GvasCursor reader, string type)
    {
        var start = reader.Position;
        var kind = GvasScalarKind.Signed;
        object value;
        switch (type)
        {
            case "BoolProperty":
                kind = GvasScalarKind.Boolean;
                var boolean = reader.Byte();
                if (boolean > 1) throw new SaveFormatException("Invalid GVAS boolean.");
                value = boolean != 0;
                break;
            case "Int8Property": value = (Int128)(sbyte)reader.Byte(); break;
            case "Int16Property": value = (Int128)BinaryPrimitives.ReadInt16LittleEndian(reader.Take(2)); break;
            case "IntProperty": value = (Int128)reader.Int32(); break;
            case "Int64Property": value = (Int128)BinaryPrimitives.ReadInt64LittleEndian(reader.Take(8)); break;
            case "UInt8Property": case "ByteProperty": kind = GvasScalarKind.Unsigned; value = (Int128)reader.Byte(); break;
            case "UInt16Property": kind = GvasScalarKind.Unsigned; value = (Int128)reader.UInt16(); break;
            case "UInt32Property": kind = GvasScalarKind.Unsigned; value = (Int128)reader.UInt32(); break;
            case "UInt64Property": kind = GvasScalarKind.Unsigned; value = (Int128)BinaryPrimitives.ReadUInt64LittleEndian(reader.Take(8)); break;
            case "FloatProperty": kind = GvasScalarKind.Float; value = (double)BinaryPrimitives.ReadSingleLittleEndian(reader.Take(4)); break;
            case "DoubleProperty": kind = GvasScalarKind.Double; value = BinaryPrimitives.ReadDoubleLittleEndian(reader.Take(8)); break;
            case "StrProperty": case "NameProperty": case "EnumProperty": case "ObjectProperty": case "InterfaceProperty":
                if (reader.Remaining < 4) throw new SaveFormatException("Truncated GVAS string length.");
                var wide = BinaryPrimitives.ReadInt32LittleEndian(reader.Data.Span.Slice(start, 4)) < 0;
                value = reader.String();
                return new GvasScalar(GvasScalarKind.String, start, reader.Position - start, value, wide);
            default: throw new UnsupportedGvasValueException($"Unsupported value type {type}.");
        }
        if (value is double number && !double.IsFinite(number))
            throw new UnsupportedGvasValueException("Non-finite floating point value.");
        return new GvasScalar(kind, start, reader.Position - start, value);
    }

    public static GvasScalar ReadText(GvasCursor reader)
    {
        _ = reader.UInt32(); // FText flags.
        var history = (sbyte)reader.Byte();
        switch (history)
        {
            case 0: // Base history: namespace, localization key, source text.
                _ = reader.String();
                _ = reader.String();
                return Read(reader, "StrProperty");
            case -1: // Culture invariant text, when present.
                if (reader.Remaining >= 4 && reader.Int32() == 1) return Read(reader, "StrProperty");
                throw new UnsupportedGvasValueException("FText has no culture invariant string.");
            default: throw new UnsupportedGvasValueException($"FText history {history} is read-only.");
        }
    }
}
