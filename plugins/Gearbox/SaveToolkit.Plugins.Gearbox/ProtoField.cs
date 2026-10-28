using Google.Protobuf;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Retains the complete original wire record, including unknown and noncanonical encodings.</summary>
internal sealed class ProtoField(int number, int wireType, ProtoSpec? spec, byte[] original, object? value, ProtoMessage? message)
{
    private object? _replacement;
    public int Number { get; } = number;
    public int WireType { get; } = wireType;
    public ProtoSpec? Spec { get; } = spec;
    public ProtoMessage? Message { get; } = message;
    public object? Value => _replacement ?? value;
    public byte[] Original { get; } = original;

    public void Set(object replacement) => _replacement = Equals(value, replacement) ? null : replacement;

    public ProtoField Clone()
    {
        var copy = new ProtoField(Number, WireType, Spec, Original, value, Message?.Clone());
        copy._replacement = _replacement;
        return copy;
    }

    public byte[] Encode()
    {
        if (Original.Length == 0 && Spec is { Repeated: false } && Value is (0UL or 0U or "")) return [];
        var nested = Message?.Encode();
        if (Original.Length > 0 && _replacement is null && (nested is null || nested.AsSpan().SequenceEqual((byte[])value!)))
        {
            return Original;
        }

        using var stream = new MemoryStream();
        using (var writer = new CodedOutputStream(stream, leaveOpen: true))
        {
            writer.WriteTag(Number, (WireFormat.WireType)WireType);
            switch (WireType)
            {
                case 0: writer.WriteUInt64((ulong)Value!); break;
                case 1: writer.WriteFixed64((ulong)Value!); break;
                case 5: writer.WriteFixed32((uint)Value!); break;
                case 2:
                    if (nested is not null) writer.WriteBytes(ByteString.CopyFrom(nested));
                    else if (Value is string text) writer.WriteString(text);
                    else writer.WriteBytes(ByteString.CopyFrom((byte[])Value!));
                    break;
                default: throw new InvalidOperationException("Cannot rewrite an opaque protobuf group.");
            }
        }
        return stream.ToArray();
    }
}
