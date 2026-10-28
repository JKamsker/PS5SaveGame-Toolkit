using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record SavedScalar(FieldSpec Spec, FieldValue Value);

internal static class ScalarReader
{
    public static SavedScalar? Read(Sav3Node node, byte[] payload, IReadOnlyList<string> names)
    {
        if (node.Type is null)
        {
            return null;
        }

        var raw = BinaryData.Slice(payload, node.ValueOffset, node.ValueSize);
        switch (node.Type)
        {
            case "Bool" when raw.Length == 1 && raw[0] <= 1:
                return new(new BoolSpec(), FieldValue.Of(raw[0] != 0));
            case "Uint8" when raw.Length == 1:
                return Integer(IntegerSpec.UInt8, raw[0]);
            case "Int8" when raw.Length == 1:
                return Integer(IntegerSpec.Int8, unchecked((sbyte)raw[0]));
            case "Uint16" when raw.Length == 2:
                return Integer(IntegerSpec.UInt16, BinaryPrimitives.ReadUInt16LittleEndian(raw));
            case "Int16" when raw.Length == 2:
                return Integer(IntegerSpec.Int16, BinaryPrimitives.ReadInt16LittleEndian(raw));
            case "Uint32" when raw.Length == 4:
                return Integer(IntegerSpec.UInt32, BinaryPrimitives.ReadUInt32LittleEndian(raw));
            case "Int32" when raw.Length == 4:
                return Integer(IntegerSpec.Int32, BinaryPrimitives.ReadInt32LittleEndian(raw));
            case "Uint64" when raw.Length == 8:
                return Integer(IntegerSpec.UInt64, BinaryPrimitives.ReadUInt64LittleEndian(raw));
            case "Int64" when raw.Length == 8:
                return Integer(IntegerSpec.Int64, BinaryPrimitives.ReadInt64LittleEndian(raw));
            case "Float" when raw.Length == 4:
                var single = BinaryPrimitives.ReadSingleLittleEndian(raw);
                return float.IsFinite(single) ? new(FloatSpec.Single, FieldValue.Of(single)) : null;
            case "Double" when raw.Length == 8:
                var number = BinaryPrimitives.ReadDoubleLittleEndian(raw);
                return double.IsFinite(number) ? new(FloatSpec.Double, FieldValue.Of(number)) : null;
            case "CName" when raw.Length == 2:
                var index = BinaryPrimitives.ReadUInt16LittleEndian(raw);
                return index > 0 && index <= names.Count ? Text(names[index - 1]) : null;
            case "CGUID" when raw.Length == 16:
                return Text(new Guid(raw).ToString());
            case "String" when raw.Length > 0 && (raw[0] & 0x80) != 0 && raw.Length == 1 + (raw[0] & 0x7F):
                return PrintableText(raw[1..]);
            case "StringAnsi" when raw.Length > 0 && raw.Length == 1 + raw[0]:
                return PrintableText(raw[1..].TrimEnd((byte)0));
            default:
                return null;
        }
    }

    private static SavedScalar Integer(IntegerSpec spec, Int128 value) => new(spec, FieldValue.Of(value));
    private static SavedScalar Text(string value) => new(new TextSpec(), FieldValue.Of(value));
    private static SavedScalar? PrintableText(ReadOnlySpan<byte> value) => value.ContainsAnyExceptInRange((byte)0x20, (byte)0x7E)
        ? null : Text(Encoding.ASCII.GetString(value));
}
