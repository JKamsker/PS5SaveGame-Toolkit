using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Checks physical framing and shared work limits before allocating parsed records.
/// Google.Protobuf deliberately truncates overflowing 32-bit tags/lengths; save framing must not.</summary>
internal static class ProtoWireValidation
{
    private const int MaximumDepth = 40;
    private const int MaximumFields = 250_000;

    public static void Validate(ReadOnlySpan<byte> bytes, GearboxSchema schema, string type)
    {
        var remaining = MaximumFields;
        var reader = new WireReader(bytes);
        Fields(ref reader, schema, type, 0, 0, ref remaining);
    }

    private static void Fields(ref WireReader reader, GearboxSchema schema, string? type,
        int depth, uint group, ref int remaining)
    {
        if (depth > MaximumDepth) throw new SaveFormatException("Gearbox protobuf nesting exceeds the supported limit.");
        while (!reader.AtEnd)
        {
            var tag = reader.Tag();
            var number = tag >> 3;
            var wire = (int)(tag & 7);
            if (wire == 4)
            {
                if (group == 0 || number != group) throw new SaveFormatException("Mismatched Gearbox protobuf end-group tag.");
                return;
            }
            Count(ref remaining);
            var spec = type is null ? null : schema.Find(type, (int)number);
            var packed = spec is { Repeated: true, WireType: not 2 } && wire == 2;
            if (spec is not null && wire != spec.WireType && !packed)
                throw new SaveFormatException($"Wrong wire type for {type}.{spec.Name}.");
            switch (wire)
            {
                case 0: _ = reader.Varint(); break;
                case 1: _ = reader.Take(sizeof(ulong)); break;
                case 5: _ = reader.Take(sizeof(uint)); break;
                case 2:
                    var data = reader.Delimited();
                    if (spec is not null && schema.IsMessage(spec.Type))
                    {
                        var child = new WireReader(data);
                        Fields(ref child, schema, spec.Type, depth + 1, 0, ref remaining);
                    }
                    else if (packed) Packed(data, spec!.WireType, ref remaining);
                    break;
                case 3:
                    // Unknown groups stay opaque, but their contents still consume the shared budgets.
                    Fields(ref reader, schema, null, depth + 1, number, ref remaining);
                    break;
            }
        }
        if (group != 0) throw new SaveFormatException("Truncated Gearbox protobuf group.");
    }

    private static void Packed(ReadOnlySpan<byte> bytes, int wire, ref int remaining)
    {
        var reader = new WireReader(bytes);
        while (!reader.AtEnd)
        {
            Count(ref remaining);
            if (wire == 0) _ = reader.Varint();
            else _ = reader.Take(wire == 1 ? sizeof(ulong) : sizeof(uint));
        }
    }

    private static void Count(ref int remaining)
    {
        if (--remaining < 0) throw new SaveFormatException("Gearbox protobuf has too many fields or packed elements.");
    }

    private ref struct WireReader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _remaining = bytes;
        public readonly bool AtEnd => _remaining.IsEmpty;

        public ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > _remaining.Length) throw new SaveFormatException("Truncated Gearbox protobuf field.");
            var result = _remaining[..count];
            _remaining = _remaining[count..];
            return result;
        }

        public ulong Varint()
        {
            const int bitsPerByte = 7;
            const int finalShift = 63;
            ulong value = 0;
            for (var shift = 0; shift <= finalShift; shift += bitsPerByte)
            {
                var next = Take(1)[0];
                if (shift == finalShift && next > 1) throw new SaveFormatException("Overflowing Gearbox protobuf varint.");
                value |= (ulong)(next & 0x7F) << shift;
                if ((next & 0x80) == 0) return value;
            }
            throw new SaveFormatException("Invalid Gearbox protobuf varint.");
        }

        public uint Tag()
        {
            var tag = Varint();
            if (tag > uint.MaxValue || (tag >> 3) == 0 || (tag & 7) > 5)
                throw new SaveFormatException("Invalid Gearbox protobuf tag.");
            return (uint)tag;
        }

        public ReadOnlySpan<byte> Delimited()
        {
            var length = Varint();
            if (length > (ulong)_remaining.Length) throw new SaveFormatException("Invalid or truncated Gearbox protobuf length.");
            return Take((int)length);
        }
    }
}
