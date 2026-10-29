using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.BaldursGate3;

internal static class AttributeDecoder
{
    private const int MaxTranslationDepth = 32;
    private const int MaxTranslationArguments = 10_000;
    private const int GuidSize = 16;
    private const int GuidSwapStart = 8;
    private const int TranslationHandleOffset = sizeof(ushort) + sizeof(int);
    internal static void Validate(ResourceAttribute attribute)
    {
        if (attribute.XmlValue is not null)
        {
            return;
        }

        var data = attribute.Data.Span;
        var size = FixedSize(attribute.Type);
        if (size >= 0 && data.Length != size)
            throw new SaveFormatException($"Invalid size for LSOF {attribute.Type} attribute.");
        if (IsString(attribute.Type))
        {
            ValidateString(data);
        }
        else if (attribute.Type is LarianAttributeType.TranslatedString or LarianAttributeType.TranslatedFSString)
        {
            var position = 0;
            ValidateTranslated(data, ref position, attribute.Type == LarianAttributeType.TranslatedFSString, 0);
            BinaryData.Require(position == data.Length, "Unexpected bytes in a translated LSOF string.");
        }
    }

    internal static FieldValue Read(ResourceAttribute attribute)
    {
        if (attribute.XmlValue is { } xml)
        {
            return FieldValue.Of(xml);
        }

        var data = attribute.Data.Span;
        if (IsString(attribute.Type))
        {
            return FieldValue.Of(BinaryData.Utf8.GetString(data.TrimEnd((byte)0)));
        }

        return attribute.Type switch
        {
            LarianAttributeType.Byte => FieldValue.Of((long)data[0]),
            LarianAttributeType.Int8 => FieldValue.Of((long)unchecked((sbyte)data[0])),
            LarianAttributeType.Short => FieldValue.Of((long)BinaryPrimitives.ReadInt16LittleEndian(data)),
            LarianAttributeType.UShort => FieldValue.Of((long)BinaryData.U16(data, 0)),
            LarianAttributeType.Int => FieldValue.Of((long)BinaryData.I32(data, 0)),
            LarianAttributeType.UInt => FieldValue.Of((ulong)BinaryData.U32(data, 0)),
            LarianAttributeType.Long or LarianAttributeType.Int64 => FieldValue.Of(unchecked((long)BinaryData.U64(data, 0))),
            LarianAttributeType.ULongLong => FieldValue.Of(BinaryData.U64(data, 0)),
            LarianAttributeType.Bool => FieldValue.Of(data[0] != 0),
            LarianAttributeType.Float => Float(BinaryPrimitives.ReadSingleLittleEndian(data)),
            LarianAttributeType.Double => Float(BinaryPrimitives.ReadDoubleLittleEndian(data)),
            LarianAttributeType.UUID => FieldValue.Of(ReadGuid(data)),
            LarianAttributeType.None => FieldValue.Of("None"),
            LarianAttributeType.TranslatedString => FieldValue.Of($"{BinaryData.Utf8.GetString(data[TranslationHandleOffset..].TrimEnd((byte)0))}; version {BinaryData.U16(data, 0)}"),
            >= LarianAttributeType.IVec2 and <= LarianAttributeType.Mat4 => FieldValue.Of(ReadVector(attribute)),
            _ => FieldValue.Of($"{data.Length:N0} bytes ({attribute.Type})"),
        };
    }

    internal static FieldSpec Spec(FieldValue value, LarianAttributeType type) => value.Kind switch
    {
        FieldValueKind.Bool => new BoolSpec(),
        FieldValueKind.Integer => type switch
        {
            LarianAttributeType.Byte => IntegerSpec.UInt8,
            LarianAttributeType.Int8 => IntegerSpec.Int8,
            LarianAttributeType.Short => IntegerSpec.Int16,
            LarianAttributeType.UShort => IntegerSpec.UInt16,
            LarianAttributeType.Int => IntegerSpec.Int32,
            LarianAttributeType.UInt => IntegerSpec.UInt32,
            LarianAttributeType.ULongLong => IntegerSpec.UInt64,
            _ => IntegerSpec.Int64,
        },
        FieldValueKind.Float => type == LarianAttributeType.Float ? FloatSpec.Single : FloatSpec.Double,
        _ => new TextSpec { Multiline = true },
    };

    private static FieldValue Float(double value) => double.IsFinite(value) ? FieldValue.Of(value) :
        FieldValue.Of(value.ToString("R", CultureInfo.InvariantCulture));

    private static string ReadGuid(ReadOnlySpan<byte> data)
    {
        Span<byte> bytes = stackalloc byte[GuidSize];
        data.CopyTo(bytes);
        // LSLib's default LSX representation swaps the last four 16-bit groups.
        for (var i = GuidSwapStart; i < bytes.Length; i += sizeof(ushort))
        {
            (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
        }

        return new Guid(bytes).ToString();
    }

    private static string ReadVector(ResourceAttribute attribute)
    {
        var values = new string[attribute.Data.Length / sizeof(int)];
        for (var i = 0; i < values.Length; i++)
        {
            var component = attribute.Data.Span[(i * sizeof(int))..];
            values[i] = attribute.Type <= LarianAttributeType.IVec4
                ? BinaryData.I32(component, 0).ToString(CultureInfo.InvariantCulture)
                : BinaryPrimitives.ReadSingleLittleEndian(component).ToString("R", CultureInfo.InvariantCulture);
        }

        return string.Join(", ", values);
    }

    private static void ValidateTranslated(ReadOnlySpan<byte> bytes, ref int position, bool formatted, int depth)
    {
        if (depth >= MaxTranslationDepth)
            throw new SaveFormatException($"LSOF translated string nesting exceeds {MaxTranslationDepth} levels.");
        BinaryData.Slice(bytes, position, sizeof(ushort));
        position += sizeof(ushort); // BG3 translation version
        ReadSizedString(bytes, ref position);
        if (!formatted)
        {
            return;
        }

        var arguments = BinaryData.I32(bytes, position);
        position += sizeof(int);
        BinaryData.Require(arguments is >= 0 and <= MaxTranslationArguments, "Invalid LSOF translated string argument count.");
        for (var i = 0; i < arguments; i++)
        {
            ReadSizedString(bytes, ref position);
            ValidateTranslated(bytes, ref position, true, depth + 1);
            ReadSizedString(bytes, ref position);
        }
    }

    private static void ReadSizedString(ReadOnlySpan<byte> data, ref int position)
    {
        var length = BinaryData.Size(BinaryData.U32(data, position));
        position += sizeof(uint);
        ValidateString(BinaryData.Slice(data, position, length));
        position += length;
    }

    private static void ValidateString(ReadOnlySpan<byte> bytes)
    {
        BinaryData.Require(!bytes.IsEmpty && bytes[^1] == 0, "LSOF string is not null-terminated.");
        _ = BinaryData.Utf8.GetCharCount(bytes);
    }

    internal static bool IsString(LarianAttributeType type) => type is
        LarianAttributeType.String or LarianAttributeType.Path or LarianAttributeType.FixedString or
        LarianAttributeType.LSString or LarianAttributeType.WString or LarianAttributeType.LSWString;

    private static int FixedSize(LarianAttributeType type) => type switch
    {
        LarianAttributeType.None => 0,
        LarianAttributeType.Byte or LarianAttributeType.Int8 or LarianAttributeType.Bool => 1,
        LarianAttributeType.Short or LarianAttributeType.UShort => 2,
        LarianAttributeType.Int or LarianAttributeType.UInt or LarianAttributeType.Float => 4,
        LarianAttributeType.Double or LarianAttributeType.Long or LarianAttributeType.Int64 or LarianAttributeType.ULongLong or
            LarianAttributeType.IVec2 or LarianAttributeType.Vec2 => 8,
        LarianAttributeType.IVec3 or LarianAttributeType.Vec3 => 12,
        LarianAttributeType.IVec4 or LarianAttributeType.Vec4 or LarianAttributeType.Mat2 or LarianAttributeType.UUID => 16,
        LarianAttributeType.Mat3 => 36,
        LarianAttributeType.Mat3x4 or LarianAttributeType.Mat4x3 => 48,
        LarianAttributeType.Mat4 => 64,
        _ => -1,
    };
}
