using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Format;

internal static class PrimitiveCodec
{
    public static bool IsSupported(int type, int size) => type switch
    {
        FieldTags.Enum => size is 1 or 2 or 4 or 8,
        FieldTags.Boolean or FieldTags.Int8 or FieldTags.UInt8 or FieldTags.Char8 => size == 1,
        FieldTags.Int16 or FieldTags.UInt16 or FieldTags.Char16 => size == 2,
        FieldTags.Int32 or FieldTags.UInt32 or FieldTags.Float32 => size == 4,
        FieldTags.Int64 or FieldTags.UInt64 or FieldTags.Float64 => size == 8,
        _ => false,
    };

    public static FieldValue Read(SaveNode node, byte[] plaintext)
    {
        var data = plaintext.AsSpan(node.Offset, node.Size);
        if (node.Type == FieldTags.Boolean) return FieldValue.Of(data[0] != 0);
        if (node.Type == FieldTags.Float32) return FieldValue.Of(BinaryPrimitives.ReadSingleLittleEndian(data));
        if (node.Type == FieldTags.Float64) return FieldValue.Of(BinaryPrimitives.ReadDoubleLittleEndian(data));
        var unsigned = IsUnsigned(node.Type);
        Int128 integer = (node.Size, unsigned) switch
        {
            (1, false) => (sbyte)data[0],
            (1, true) => data[0],
            (2, false) => BinaryPrimitives.ReadInt16LittleEndian(data),
            (2, true) => BinaryPrimitives.ReadUInt16LittleEndian(data),
            (4, false) => BinaryPrimitives.ReadInt32LittleEndian(data),
            (4, true) => BinaryPrimitives.ReadUInt32LittleEndian(data),
            (8, false) => BinaryPrimitives.ReadInt64LittleEndian(data),
            (8, true) => BinaryPrimitives.ReadUInt64LittleEndian(data),
            _ => throw new InvalidOperationException("Unsupported primitive size."),
        };
        return FieldValue.Of(integer);
    }

    public static FieldSpec Spec(SaveNode node)
    {
        if (node.Type == FieldTags.Boolean) return new BoolSpec();
        if (node.Type == FieldTags.Float32) return FloatSpec.Single;
        if (node.Type == FieldTags.Float64) return FloatSpec.Double;
        var bits = node.Size * 8;
        return IsUnsigned(node.Type)
            ? new IntegerSpec(0, ((Int128)1 << bits) - 1)
            : new IntegerSpec(-((Int128)1 << (bits - 1)), ((Int128)1 << (bits - 1)) - 1);
    }

    public static string? Validate(SaveNode node, FieldValue value)
    {
        var spec = Spec(node);
        if (spec is BoolSpec) return value.Kind == FieldValueKind.Bool ? null : "Expected a boolean.";
        if (spec is FloatSpec floating)
        {
            if (value.Kind != FieldValueKind.Float || !double.IsFinite(value.AsDouble())) return "Expected a finite float.";
            if (floating.SinglePrecision && !float.IsFinite((float)value.AsDouble())) return "Float32 overflow.";
            return null;
        }
        var integer = (IntegerSpec)spec;
        return value.Kind == FieldValueKind.Integer && value.AsInteger() >= integer.Min && value.AsInteger() <= integer.Max
            ? null : "Integer is outside the stored type's range.";
    }

    public static void Write(SaveNode node, byte[] plaintext, FieldValue value)
    {
        var data = plaintext.AsSpan(node.Offset, node.Size);
        if (node.Type == FieldTags.Boolean) { data[0] = value.AsBool() ? (byte)1 : (byte)0; return; }
        if (node.Type == FieldTags.Float32) { BinaryPrimitives.WriteSingleLittleEndian(data, (float)value.AsDouble()); return; }
        if (node.Type == FieldTags.Float64) { BinaryPrimitives.WriteDoubleLittleEndian(data, value.AsDouble()); return; }
        // Validate has already checked signedness/range; write the low bits without changing representation.
        var bits = unchecked((ulong)value.AsInteger());
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(bits >> (i * 8));
    }

    private static bool IsUnsigned(int type) => type is FieldTags.UInt8 or FieldTags.UInt16 or FieldTags.UInt32 or
        FieldTags.UInt64 or FieldTags.Char8 or FieldTags.Char16;
}
