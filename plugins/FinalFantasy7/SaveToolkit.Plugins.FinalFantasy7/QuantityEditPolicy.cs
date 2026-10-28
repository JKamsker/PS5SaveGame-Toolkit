using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Mapped existing non-equipment counts are editable; instance-bearing gear remains protected.</summary>
internal sealed class QuantityEditPolicy
{
    private const int MaximumQuantity = 9_999;
    private const int MaximumGil = 9_999_999;
    private readonly IntegerSpec?[] _ranges = new IntegerSpec?[GameplayFields.InventoryCount];

    public QuantityEditPolicy(byte[] bytes, Ff7Schema schema)
    {
        if (bytes.Length == Ff7Schema.CommonFileLength)
        {
            return;
        }

        var layout = GameplayLayout.For(schema);
        var offset = layout.InventoryOffset;
        var stride = layout.InventoryStride;
        var gilType = layout.GilType;
        var records = EmbeddedCatalogs.Records(schema, "ff7r-items")
            .Where(record => record.Category is not ("Weapons" or "Armor" or "Accessories" or "Chocobo Gear") &&
                !record.Name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(record => uint.Parse(record.Key, CultureInfo.InvariantCulture));
        var ids = Enumerable.Range(0, _ranges.Length)
            .Select(slot => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + slot * stride))).ToArray();
        var observedCaps = Enumerable.Range(0, _ranges.Length).Where(slot => records.ContainsKey(ids[slot]))
            .GroupBy(slot => records[ids[slot]].Category!)
            .ToDictionary(group => group.Key, group => Math.Max(1, group.Max(slot =>
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + slot * stride + GameplayLayout.QuantityDelta)))));
        var uniqueGil = ids.Count(id => id == gilType) == 1;
        for (var slot = 0; slot < _ranges.Length; slot++)
        {
            var quantity = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + slot * stride + GameplayLayout.QuantityDelta));
            if (quantity < 0 || !(ids[slot] == gilType ? uniqueGil : records.ContainsKey(ids[slot])))
            {
                continue;
            }

            _ranges[slot] = IntegerSpec.Int32 with
            {
                Min = 0, Max = Math.Max(quantity, ids[slot] == gilType ? MaximumGil :
                    records[ids[slot]].Category == "Items" ? MaximumQuantity : observedCaps[records[ids[slot]].Category!]),
            };
        }
    }

    public IntegerSpec? Range(int slot) => _ranges[slot];
}
