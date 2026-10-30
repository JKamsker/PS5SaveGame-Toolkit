using System.Text;
using System.Text.Encodings.Web;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson;

/// <summary>Counts the exact UTF-8 bytes produced by the default JSON scalar encoder.</summary>
internal static class JsonScalarEncoding
{
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.Default;

    public static int EncodedLength(FieldValue value) => value.Kind switch
    {
        FieldValueKind.Text => checked(StringLength(value.AsString())),
        FieldValueKind.ExactNumber => Encoding.UTF8.GetByteCount(value.AsExactNumber()),
        FieldValueKind.Bool => value.AsBool() ? 4 : 5,
        FieldValueKind.Null => 4,
        _ => throw new SaveFormatException("Unsupported JSON scalar type."),
    };

    private static int StringLength(string value)
    {
        long length = 2;
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (char.IsHighSurrogate(character))
            {
                if (++i >= value.Length || !char.IsLowSurrogate(value[i]))
                    throw new SaveFormatException("Text contains an unpaired Unicode surrogate.");
                var scalar = char.ConvertToUtf32(character, value[i]);
                length += Encoder.WillEncode(scalar) ? 12 : 4;
                continue;
            }
            if (char.IsLowSurrogate(character))
                throw new SaveFormatException("Text contains an unpaired Unicode surrogate.");
            if (character is '\\' or '\b' or '\t' or '\n' or '\f' or '\r')
                length += 2;
            else if (character < '\u0020' || Encoder.WillEncode(character))
                length += 6;
            else
                length += Utf8Length(character);
        }
        return checked((int)length);
    }

    private static int Utf8Length(char character) => character switch
    {
        <= '\u007F' => 1,
        <= '\u07FF' => 2,
        _ => 3,
    };
}
