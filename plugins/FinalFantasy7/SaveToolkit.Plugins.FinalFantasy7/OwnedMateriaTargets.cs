using System.Buffers.Binary;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Pure current-model join. A detail index, inventory index and native instance are distinct.</summary>
internal sealed class OwnedMateriaTargets(byte[] bytes, Ff7Schema schema, ReferenceData references)
{
    internal const int InventoryOffset = 0x60A6C;
    internal const int DetailOffset = 0xBFA68;
    private const int TypeDelta = sizeof(uint);
    private const int OwnerDelta = 0x11;

    public IReadOnlyList<PresetCandidate> Entries(uint requestedType)
    {
        if (schema.IsRemake) return [];
        var details = Enumerable.Range(0, GameplayFields.MateriaCount)
            .GroupBy(slot => U32(DetailOffset + slot * GameplayLayout.MateriaStride))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var inventory = Enumerable.Range(0, GameplayFields.MateriaCount)
            .Where(slot => U32(InventoryOffset + slot * GameplayLayout.MateriaStride) != uint.MaxValue)
            .GroupBy(slot => U32(InventoryOffset + slot * GameplayLayout.MateriaStride))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var result = new List<PresetCandidate>();
        foreach (var (instance, slots) in inventory.OrderBy(pair => pair.Key))
        {
            // One semantic instance, with every inventory placement retained as diagnostics.
            // Duplicates never select an inventory record or expand into a Cartesian product.
            var matchingSlots = slots.Where(slot => U32(InventoryOffset + slot * GameplayLayout.MateriaStride + TypeDelta) == requestedType).ToArray();
            if (matchingSlots.Length == 0 || EmptySlots.IsEmptyType(requestedType, true, schema)) continue;
            var matches = details.GetValueOrDefault(instance) ?? [];
            var status = Status(slots, matches, requestedType);
            if (matches.Length == 0)
                result.Add(new("", null, requestedType, instance, status, references.Source("ff7r-materia", requestedType))
                    { OwnedInventorySlots = matchingSlots });
            foreach (var detailSlot in matches)
            {
                var owner = bytes[DetailOffset + detailSlot * GameplayLayout.MateriaStride + OwnerDelta];
                result.Add(new($"materia/{detailSlot}", detailSlot, requestedType, instance, status,
                    references.Source("ff7r-materia", requestedType), owner < Ff7Schema.CharacterCount ? owner : null)
                    { OwnedInventorySlots = matchingSlots,
                        DetailTypeId = U32(DetailOffset + detailSlot * GameplayLayout.MateriaStride + TypeDelta) });
            }
        }
        return result;
    }

    private PresetDetailStatus Status(int[] inventorySlots, int[] detailSlots, uint type)
    {
        if (detailSlots.Length == 0) return PresetDetailStatus.MissingDetail;
        if (detailSlots.Length > 1) return PresetDetailStatus.DuplicateDetail;
        if (inventorySlots.Length > 1) return PresetDetailStatus.DuplicateInventory;
        if (U32(InventoryOffset + inventorySlots.Single() * GameplayLayout.MateriaStride + 2 * sizeof(uint)) != 1)
            return PresetDetailStatus.InvalidOwnedCount;
        return U32(DetailOffset + detailSlots.Single() * GameplayLayout.MateriaStride + TypeDelta) == type
            ? PresetDetailStatus.Unique : PresetDetailStatus.TypeMismatch;
    }

    public static PresetTarget Capture(int detailSlot, IReadOnlyList<PresetCandidate> entries)
    {
        var matches = entries.Where(entry => entry.RowKey == $"materia/{detailSlot}").ToArray();
        if (matches is not [{ Resolved: true, InstanceId: { } instance, TypeId: var type }])
            throw new ArgumentException("Selected materia needs a unique owned inventory/detail join; no positional fallback is permitted.");
        return new(PresetIdentityDomain.MateriaInstance, instance, type) { Source = matches[0].Source };
    }

    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
