using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy16;

// Read-only aliases must stay live and be included in change review and undo outcomes.
internal static class FaithDerivedValues
{
    public static Dictionary<string, FieldValue> Capture(byte[] bytes, FaithInventory inventory, FaithWriteMap writes)
    {
        var values = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        Bank(inventory.Items, "main");
        Bank(inventory.SecondaryItems, "secondary");
        for (var index = 0; index < FaithContextLayout.SummonCount; index++)
            if (writes.Find($"context/summons/{index}") is { } summon)
                values.Add($"context/feats/{index}", summon.Get(reference: true));
        return values;

        void Bank(IReadOnlyList<FaithItem> items, string bank)
        {
            foreach (var item in items.Where(item => FaithCapacityContext.IsUpgrade(item.Id)))
                values.Add(UpgradeId(item, bank), FieldValue.Of(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(item.Offset + sizeof(int)))));
        }
    }

    public static string UpgradeId(FaithItem item, string bank) => $"capacity/{item.Id}/{bank}/{item.Slot}";
}
