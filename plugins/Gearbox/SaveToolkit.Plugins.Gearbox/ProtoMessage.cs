using System.Text;
using Google.Protobuf;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed class ProtoMessage(GearboxSchema schema, string type, List<ProtoField> fields)
{
    private const int MaxDepth = 40;
    private const int MaxFields = 250_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public string Type { get; } = type;
    public IReadOnlyList<ProtoField> Fields => fields;
    public GearboxSchema Schema { get; } = schema;

    public static ProtoMessage Parse(byte[] bytes, GearboxSchema schema, string type)
    {
        try
        {
            var remaining = MaxFields;
            return ParseCore(bytes, schema, type, 0, ref remaining);
        }
        catch (Exception ex) when (ex is InvalidProtocolBufferException or DecoderFallbackException or OverflowException)
        {
            throw new SaveFormatException("Invalid Gearbox protobuf payload (wrong masks, damaged or truncated save).", ex);
        }
    }

    private static ProtoMessage ParseCore(byte[] bytes, GearboxSchema schema, string type, int depth, ref int remaining)
    {
        if (depth > MaxDepth) throw new SaveFormatException("Gearbox protobuf nesting exceeds the supported limit.");
        using var reader = new CodedInputStream(bytes);
        var fields = new List<ProtoField>();
        var singularMessages = new HashSet<int>();
        while (!reader.IsAtEnd)
        {
            if (--remaining < 0) throw new SaveFormatException("Gearbox protobuf has too many fields.");
            var start = checked((int)reader.Position);
            var tag = reader.ReadTag();
            var number = WireFormat.GetTagFieldNumber(tag);
            var wire = (int)WireFormat.GetTagWireType(tag);
            if (number == 0 || wire is 4 or > 5) throw new SaveFormatException("Invalid Gearbox protobuf tag.");
            var spec = schema.Find(type, number);
            if (spec is { Repeated: false } && schema.IsMessage(spec.Type) && !singularMessages.Add(number))
                throw new SaveFormatException("Split singular protobuf messages are not supported safely by this editor.");
            var packed = spec is { Repeated: true, WireType: not 2 } && wire == 2;
            if (spec is not null && wire != spec.WireType && !packed)
                throw new SaveFormatException($"Wrong wire type for {type}.{spec.Name}.");
            object? value = null;
            ProtoMessage? child = null;
            switch (wire)
            {
                case 0: value = reader.ReadUInt64(); break;
                case 1: value = reader.ReadFixed64(); break;
                case 5: value = reader.ReadFixed32(); break;
                case 2:
                    var data = reader.ReadBytes().ToByteArray();
                    value = data;
                    if (spec?.Type == "string") value = StrictUtf8.GetString(data);
                    else if (spec is not null && schema.IsMessage(spec.Type))
                        child = ParseCore(data, schema, spec.Type, depth + 1, ref remaining);
                    else if (packed) ValidatePacked(data, spec!.WireType);
                    break;
                default: reader.SkipLastField(); break; // Unknown legacy groups remain verbatim.
            }
            fields.Add(new ProtoField(number, wire, spec, bytes[start..checked((int)reader.Position)], value, child));
        }
        return new ProtoMessage(schema, type, fields);
    }

    private static void ValidatePacked(byte[] bytes, int wire)
    {
        using var reader = new CodedInputStream(bytes);
        while (!reader.IsAtEnd)
        {
            if (wire == 0) _ = reader.ReadUInt64();
            else if (wire == 1) _ = reader.ReadFixed64();
            else _ = reader.ReadFixed32();
        }
    }

    public IEnumerable<ProtoField> All(string name)
    {
        var tag = Schema.Tag(Type, name);
        return fields.Where(f => f.Number == tag);
    }
    public IEnumerable<ProtoMessage> Children(string name) => All(name).Select(f => f.Message!);
    public ProtoMessage? Child(string name) => Children(name).LastOrDefault();
    public ProtoField? Last(string name) => All(name).LastOrDefault();
    public long Integer(string name) => unchecked((long)(ulong)(Last(name)?.Value ?? 0UL));
    public string Text(string name) => (string?)Last(name)?.Value ?? string.Empty;
    public float Float(string name) => BitConverter.UInt32BitsToSingle((uint)(Last(name)?.Value ?? 0U));
    public ProtoMessage Clone() => new(Schema, Type, fields.Select(f => f.Clone()).ToList());

    public long CategoryQuantity(string list, uint hash) => Children(list)
        .FirstOrDefault(m => m.Integer("base_category_definition_hash") == hash)?.Integer("quantity") ?? 0;

    public void SetCategoryQuantity(string list, uint hash, long quantity)
    {
        var field = All(list).FirstOrDefault(f => f.Message?.Integer("base_category_definition_hash") == hash);
        if (field is null)
        {
            if (quantity == 0) return;
            var spec = Schema.Spec(Type, list);
            var child = new ProtoMessage(Schema, spec.Type, []);
            child.SetInteger("base_category_definition_hash", hash);
            field = new ProtoField(Schema.Tag(Type, list), 2, spec, [], Array.Empty<byte>(), child);
            fields.Add(field);
        }
        if (quantity == 0 && field.Original.Length == 0) fields.Remove(field);
        else field.Message!.SetInteger("quantity", quantity);
    }

    public void SetInteger(string name, long value) => Set(name, unchecked((ulong)value));
    public void SetText(string name, string value) => Set(name, value);
    public void SetFloat(string name, float value)
    {
        // FieldValue considers both zero signs equal. Preserve imported bits on a semantic no-op
        // so bytes cannot change without a reported value change and undo snapshot.
        if (Last(name) is not null && Float(name).Equals(value)) return;
        Set(name, BitConverter.SingleToUInt32Bits(value));
    }
    public void Add(ProtoField field) => fields.Add(field);
    public void Remove(ProtoField field)
    {
        if (!fields.Remove(field)) throw new InvalidOperationException("Item is no longer present.");
    }

    private void Set(string name, object value)
    {
        var field = Last(name);
        if (field is null)
        {
            var spec = Schema.Spec(Type, name);
            field = new ProtoField(Schema.Tag(Type, name), spec.WireType, spec, [], null, null);
            fields.Add(field);
        }
        field.Set(value);
    }

    public byte[] Encode()
    {
        using var output = new MemoryStream();
        foreach (var field in fields) output.Write(field.Encode());
        return output.ToArray();
    }
}
