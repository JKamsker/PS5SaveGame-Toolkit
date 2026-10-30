using System.Text;
using Google.Protobuf;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed class ProtoMessage(GearboxSchema schema, string type, List<ProtoField> fields)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    // Built with the parsed/cloned model, never by metadata observation. Keep actual wire order
    // within each known tag so scalar last-occurrence and repeated-record ordinals stay exact.
    private readonly Dictionary<int, List<ProtoField>> _byTag = Index(schema, type, fields);
    internal int IndexConstructionVisits { get; } = fields.Count;
    internal long IndexedFieldReads { get; private set; }
    internal long TargetedFieldVisits { get; private set; }
    internal void CountTargetedVisit() => TargetedFieldVisits++;
    public string Type { get; } = type;
    public IReadOnlyList<ProtoField> Fields => fields;
    public GearboxSchema Schema { get; } = schema;

    public static ProtoMessage Parse(byte[] bytes, GearboxSchema schema, string type)
    {
        try
        {
            ProtoWireValidation.Validate(bytes, schema, type);
            return ParseCore(bytes, schema, type);
        }
        catch (Exception ex) when (ex is InvalidProtocolBufferException or DecoderFallbackException or OverflowException)
        {
            throw new SaveFormatException("Invalid Gearbox protobuf payload (wrong masks, damaged or truncated save).", ex);
        }
    }

    private static ProtoMessage ParseCore(byte[] bytes, GearboxSchema schema, string type)
    {
        using var reader = new CodedInputStream(bytes);
        var fields = new List<ProtoField>();
        var singularMessages = new HashSet<int>();
        while (!reader.IsAtEnd)
        {
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
                        child = ParseCore(data, schema, spec.Type);
                    break;
                default: reader.SkipLastField(); break; // Unknown legacy groups remain verbatim.
            }
            fields.Add(new ProtoField(number, wire, spec, bytes[start..checked((int)reader.Position)], value, child));
        }
        return new ProtoMessage(schema, type, fields);
    }

    public IEnumerable<ProtoField> All(string name)
    {
        var tag = Schema.Tag(Type, name);
        return _byTag.GetValueOrDefault(tag) ?? Enumerable.Empty<ProtoField>();
    }
    public IEnumerable<ProtoMessage> Children(string name) => All(name).Select(f => f.Message!);
    public ProtoMessage? Child(string name) => Last(name)?.Message;
    public ProtoField? Last(string name) => Indexed(name, null);
    internal ProtoField? FieldAt(string name, int index) => Indexed(name, index);

    private ProtoField? Indexed(string name, int? index)
    {
        var entries = _byTag.GetValueOrDefault(Schema.Tag(Type, name));
        var position = index ?? (entries?.Count - 1 ?? -1);
        if (entries is null || (uint)position >= (uint)entries.Count) return null;
        IndexedFieldReads++;
        return entries[position];
    }
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
            Add(field);
        }
        if (quantity == 0 && field.Original.Length == 0) Remove(field);
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
    public void Add(ProtoField field)
    {
        fields.Add(field);
        AddIndexed(_byTag, Schema, Type, field);
    }
    public void Remove(ProtoField field)
    {
        if (!fields.Remove(field)) throw new InvalidOperationException("Item is no longer present.");
        if (_byTag.TryGetValue(field.Number, out var entries)) entries.Remove(field);
    }

    private void Set(string name, object value)
    {
        var field = Last(name);
        if (field is null)
        {
            var spec = Schema.Spec(Type, name);
            field = new ProtoField(Schema.Tag(Type, name), spec.WireType, spec, [], null, null);
            Add(field);
        }
        field.Set(value);
    }

    private static Dictionary<int, List<ProtoField>> Index(GearboxSchema schema, string type, List<ProtoField> fields)
    {
        var index = new Dictionary<int, List<ProtoField>>();
        foreach (var field in fields) AddIndexed(index, schema, type, field);
        return index;
    }

    private static void AddIndexed(Dictionary<int, List<ProtoField>> index, GearboxSchema schema, string type, ProtoField field)
    {
        if (schema.Find(type, field.Number) is null) return;
        if (!index.TryGetValue(field.Number, out var entries)) index[field.Number] = entries = [];
        entries.Add(field);
    }

    public byte[] Encode()
    {
        using var output = new MemoryStream();
        foreach (var field in fields) output.Write(field.Encode());
        return output.ToArray();
    }
}
