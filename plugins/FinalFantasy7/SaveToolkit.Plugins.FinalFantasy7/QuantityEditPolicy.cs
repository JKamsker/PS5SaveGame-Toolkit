using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Only ordinary item stacks and an unambiguous gil row retain quantity editing.</summary>
internal sealed class QuantityEditPolicy
{
    private const int MaximumQuantity = 9_999;
    private const int MaximumGil = 9_999_999;
    private readonly IntegerSpec?[] _ranges = new IntegerSpec?[GameplayFields.InventoryCount];

    public QuantityEditPolicy(byte[] bytes, Ff7Schema schema)
    {
        if (schema.IsDemo || bytes.Length == Ff7Schema.CommonFileLength)
        {
            return;
        }

        var layout = GameplayLayout.For(schema);
        var offset = layout.InventoryOffset;
        var stride = layout.InventoryStride;
        var gilType = layout.GilType;
        var ordinary = EmbeddedCatalogs.Records(schema, "ff7r-items")
            .Where(record => record.Category == "Items" && !record.Name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
            .Select(record => uint.Parse(record.Key, CultureInfo.InvariantCulture)).ToHashSet();
        var ids = Enumerable.Range(0, _ranges.Length)
            .Select(slot => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + slot * stride))).ToArray();
        var uniqueGil = ids.Count(id => id == gilType) == 1;
        for (var slot = 0; slot < _ranges.Length; slot++)
        {
            var quantity = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + slot * stride + GameplayLayout.QuantityDelta));
            if (quantity < 0 || !(ids[slot] == gilType ? uniqueGil : ordinary.Contains(ids[slot])))
            {
                continue;
            }

            _ranges[slot] = IntegerSpec.Int32 with
            {
                Min = 0, Max = Math.Max(quantity, ids[slot] == gilType ? MaximumGil : MaximumQuantity),
            };
        }
    }

    public IntegerSpec? Range(int slot) => _ranges[slot];
}
