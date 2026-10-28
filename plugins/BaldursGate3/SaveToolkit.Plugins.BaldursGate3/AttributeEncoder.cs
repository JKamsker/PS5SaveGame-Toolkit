using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Only primitive LSF attributes are editable; opaque structures are never interpreted.</summary>
internal static class AttributeEncoder
{
    internal const int MaxTextLength = 1_000_000;

    internal static bool CanWrite(ResourceAttribute attribute) => attribute.XmlValue is null && attribute.Type switch
    {
        LarianAttributeType.Byte or LarianAttributeType.Int8 or LarianAttributeType.Short or LarianAttributeType.UShort or
        LarianAttributeType.Int or LarianAttributeType.UInt or LarianAttributeType.Long or LarianAttributeType.Int64 or
        LarianAttributeType.ULongLong or LarianAttributeType.Bool or LarianAttributeType.String or LarianAttributeType.Path or
        LarianAttributeType.FixedString or LarianAttributeType.LSString or LarianAttributeType.WString or LarianAttributeType.LSWString => true,
        LarianAttributeType.Float or LarianAttributeType.Double => AttributeDecoder.Read(attribute).Kind == FieldValueKind.Float,
        _ => false,
    };

    internal static byte[] Write(ResourceAttribute attribute, FieldValue value)
    {
        BinaryData.Require(CanWrite(attribute), "This LSOF attribute type is read-only.");
        // Returning the original encoding preserves noncanonical true bytes and extra string NULs on undo.
        if (value == AttributeDecoder.Read(attribute))
        {
            return attribute.Data.ToArray();
        }

        if (AttributeDecoder.IsString(attribute.Type))
        {
            var text = value.AsString();
            BinaryData.Require(text.Length <= MaxTextLength && !text.Contains('\0'), "LSOF text is too long or contains a NUL character.");
            return [.. BinaryData.Utf8.GetBytes(text), 0];
        }

        var data = new byte[attribute.Data.Length];
        switch (attribute.Type)
        {
            case LarianAttributeType.Byte: data[0] = checked((byte)value.AsInteger()); break;
            case LarianAttributeType.Int8: data[0] = unchecked((byte)checked((sbyte)value.AsInteger())); break;
            case LarianAttributeType.Short: BinaryPrimitives.WriteInt16LittleEndian(data, checked((short)value.AsInteger())); break;
            case LarianAttributeType.UShort: BinaryPrimitives.WriteUInt16LittleEndian(data, checked((ushort)value.AsInteger())); break;
            case LarianAttributeType.Int: BinaryPrimitives.WriteInt32LittleEndian(data, value.AsInt32()); break;
            case LarianAttributeType.UInt: BinaryPrimitives.WriteUInt32LittleEndian(data, value.AsUInt32()); break;
            case LarianAttributeType.Long or LarianAttributeType.Int64: BinaryPrimitives.WriteInt64LittleEndian(data, value.AsInt64()); break;
            case LarianAttributeType.ULongLong: BinaryPrimitives.WriteUInt64LittleEndian(data, value.AsUInt64()); break;
            case LarianAttributeType.Bool: data[0] = value.AsBool() ? (byte)1 : (byte)0; break;
            case LarianAttributeType.Float:
                BinaryData.Require(float.IsFinite((float)value.AsDouble()), "LSOF float must be finite and fit float32.");
                BinaryPrimitives.WriteSingleLittleEndian(data, (float)value.AsDouble());
                break;
            case LarianAttributeType.Double:
                BinaryData.Require(double.IsFinite(value.AsDouble()), "LSOF double must be finite.");
                BinaryPrimitives.WriteDoubleLittleEndian(data, value.AsDouble());
                break;
            default: throw new InvalidOperationException("Unsupported primitive edit.");
        }

        AttributeDecoder.Validate(attribute with { Data = data });
        return data;
    }
}
