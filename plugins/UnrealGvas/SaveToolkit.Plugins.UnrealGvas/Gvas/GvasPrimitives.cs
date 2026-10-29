using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

internal static class GvasPrimitives
{
    public static GvasScalar Read(ref BoundedBinaryCursor reader, string type)
    {
        var start = reader.Position;
        var kind = GvasScalarKind.Signed;
        object value;
        switch (type)
        {
            case "BoolProperty":
                kind = GvasScalarKind.Boolean;
                var boolean = reader.ReadByte();
                if (boolean > 1) throw new SaveFormatException("Invalid GVAS boolean.");
                value = boolean != 0;
                break;
            case "Int8Property": value = (Int128)(sbyte)reader.ReadByte(); break;
            case "Int16Property": value = (Int128)reader.ReadInt16LE(); break;
            case "IntProperty": value = (Int128)reader.ReadInt32LE(); break;
            case "Int64Property": value = (Int128)reader.ReadInt64LE(); break;
            case "UInt8Property": case "ByteProperty": kind = GvasScalarKind.Unsigned; value = (Int128)reader.ReadByte(); break;
            case "UInt16Property": kind = GvasScalarKind.Unsigned; value = (Int128)reader.ReadUInt16LE(); break;
            case "UInt32Property": kind = GvasScalarKind.Unsigned; value = (Int128)reader.ReadUInt32LE(); break;
            case "UInt64Property": kind = GvasScalarKind.Unsigned; value = (Int128)reader.ReadUInt64LE(); break;
            case "FloatProperty": kind = GvasScalarKind.Float; value = (double)reader.ReadSingleLE(); break;
            case "DoubleProperty": kind = GvasScalarKind.Double; value = reader.ReadDoubleLE(); break;
            case "StrProperty": case "NameProperty": case "EnumProperty": case "ObjectProperty": case "InterfaceProperty":
                if (reader.Remaining < 4) throw new SaveFormatException("Truncated GVAS string length.");
                var wide = reader.ReadInt32LE() < 0;
                reader.Position = start;
                value = reader.String();
                return new GvasScalar(GvasScalarKind.String, start, reader.Position - start, value, wide);
            default: throw new UnsupportedGvasValueException($"Unsupported value type {type}.");
        }
        if (value is double number && !double.IsFinite(number))
            throw new UnsupportedGvasValueException("Non-finite floating point value.");
        return new GvasScalar(kind, start, reader.Position - start, value);
    }

    public static GvasScalar ReadText(ref BoundedBinaryCursor reader)
    {
        _ = reader.ReadUInt32LE(); // FText flags.
        var history = (sbyte)reader.ReadByte();
        switch (history)
        {
            case 0: // Base history: namespace, localization key, source text.
                _ = reader.String();
                _ = reader.String();
                return Read(ref reader, "StrProperty");
            case -1: // Culture invariant text, when present.
                if (reader.Remaining >= 4 && reader.ReadInt32LE() == 1) return Read(ref reader, "StrProperty");
                throw new UnsupportedGvasValueException("FText has no culture invariant string.");
            default: throw new UnsupportedGvasValueException($"FText history {history} is read-only.");
        }
    }
}
