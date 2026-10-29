using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveToolkit.Sdk.References;

/// <summary>
/// Shares record text and attribute keys as JSON is read. This lets the catalog reuse each parsed
/// attributes dictionary instead of allocating another dictionary just to replace its string keys.
/// Used only for materialization of already validated immutable embedded records.
/// </summary>
internal sealed class PooledReferenceStringConverter(ReferenceStringPool strings) : JsonConverter<string>
{
    private const int StackBufferLength = 256;

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadShared(ref reader);

    public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadShared(ref reader);

    private string ReadShared(ref Utf8JsonReader reader)
    {
        // The encoded token length bounds the decoded UTF-16 length, including escaped text.
        var length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        char[]? rented = null;
        Span<char> buffer = length <= StackBufferLength
            ? stackalloc char[StackBufferLength]
            : rented = ArrayPool<char>.Shared.Rent(length);
        try
        {
            var count = reader.CopyString(buffer);
            return strings.Get(buffer[..count]);
        }
        finally
        {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value);
}
