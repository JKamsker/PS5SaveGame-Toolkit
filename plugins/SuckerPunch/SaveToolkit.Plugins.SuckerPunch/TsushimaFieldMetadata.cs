using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Targeted canonical bindings for existing fixed-width Tsushima fields.</summary>
internal sealed class TsushimaFieldMetadata(byte[] bytes, TsushimaInventory? inventory, TsushimaPlayTime? timer)
{
    internal delegate void Register(FieldMetadata metadata, Func<FieldValue> get,
        Action<FieldValue>? set = null, Func<FieldValue, string?>? validate = null);

    public IntegerSpec QuantitySpec(InventoryRecord record) => new(0, Math.Max(Word(record.QuantityOffset), InventoryRecord.MaximumQuantity));

    public void Bind(IEnumerable<string> ids, Register register)
    {
        foreach (var id in ids)
        {
            if (id == TsushimaIds.ChecksumValue)
            {
                register(new(id, "Container checksum", IntegerSpec.UInt32 with { ReadOnly = true, Format = IntegerFormat.Hex })
                    { BindingKind = FieldBindingKind.Derived }, () => FieldValue.Of((ulong)Word(8)));
            }
            else if (id == TsushimaPlayTime.ValueId && timer is not null)
                register(new(id, "Play time", timer.Spec), timer.Read, timer.Write, timer.Validate);
            else if (inventory?.Records.FirstOrDefault(record => record.ValueId == id || record.MirrorValueId == id) is { } record)
            {
                if (id == record.ValueId)
                {
                    var writable = record.CanEdit(bytes);
                    register(new(id, record.Name, QuantitySpec(record) with { ReadOnly = !writable }),
                        () => FieldValue.Of((ulong)Word(record.QuantityOffset)),
                        writable ? value => record.WriteQuantity(bytes, value.AsUInt32()) : null, ValidateQuantity);
                }
                else if (record.IsAmmo)
                    register(new(id, $"{record.Name} count copy", IntegerSpec.UInt32 with { ReadOnly = true })
                        { BindingKind = FieldBindingKind.Derived }, () => FieldValue.Of((ulong)Word(record.QuantityOffset + sizeof(uint))));
            }
        }
    }

    public FieldNode Quantity(InventoryRecord record, IReferenceCatalog? items, Register register)
    {
        var editable = record.CanEdit(bytes);
        var reference = items?.Find(record.Name);
        Bind([record.ValueId], register);
        return new FieldNode(record.ValueId, reference?.Name ?? record.Name,
            QuantitySpec(record) with { ReadOnly = !editable }, FieldValue.Of((ulong)Word(record.QuantityOffset))) with
        {
            Confidence = record.IsAmmo ? Confidence.Verified : Confidence.Inferred,
            Description = $"Named record at 0x{record.Start:X}; count at 0x{record.QuantityOffset:X}. Not validated on console. " +
                (record.IsAmmo ? "Apollo/Save Wizard targets these two count words." :
                "Resource count inferred from the named record and consistent slot values; raw identifier retained.") +
                (reference is null ? "" : $" {reference.Description} {reference.AttributionText}"),
            Warning = editable ? TsushimaQuantityPlanning.CapacityWarning :
                "Unmapped, locked, oversized, or inconsistent record: inspection only.",
        };
    }

    public FieldNode Mirror(InventoryRecord record, Register register)
    {
        Bind([record.MirrorValueId], register);
        return new FieldNode(record.MirrorValueId, $"{record.Name} count copy", IntegerSpec.UInt32 with { ReadOnly = true },
            FieldValue.Of((ulong)Word(record.QuantityOffset + sizeof(uint))))
        {
            Confidence = Confidence.Verified,
            Description = "Apollo targets both count words; equal in all supplied samples. Derived from quantity edits; not validated on console.",
        };
    }

    public static string? ValidateQuantity(FieldValue value) => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 ||
        value.AsInteger() > InventoryRecord.MaximumQuantity ? "Quantity must be between 0 and 32767." : null;
    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
