namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record LsmfStack(int GroupRow, int EntityRow, IReadOnlyList<int> AmountOffsets, long Original)
{
    internal string Key => $"stack/{GroupRow}/member/{EntityRow}";
}

internal sealed record LsmfSlot(int ContainerRow, int Index, int EntityRow, int Slot, int SlotOffset, int KeyOffset)
{
    internal string Key => $"container/{ContainerRow}/entry/{Index}";
}

/// <summary>Explicit current slot and stack-member references; no transform/owner heuristic.</summary>
internal static class LsmfInventory
{
    private const string Inventory = "game.inventory.";

    internal static IReadOnlyList<LsmfStack> Stacks(LsmfAccess access)
    {
        var groups = access.Column(Inventory + "v0.NewStackComponent", 8);
        var entries = access.Column(Inventory + "v0.StackEntry", 8);
        var records = access.Column(Inventory + "v0.Stack", 32);
        if (groups is null || entries is null || records is null) return [];
        BinaryData.Require(groups.Rows <= 100_000, "Too many LSMF stack groups.");
        var result = new List<LsmfStack>();
        var usedRecords = new HashSet<int>();
        var usedAmounts = new HashSet<int>();
        var usedMembers = new HashSet<int>();
        var bytes = access.Model.Bytes.Span;
        for (var group = 0; group < groups.Rows; group++)
        {
            var record = access.Arena(BinaryData.U64(bytes, groups.Offset + group * 8), 32);
            BinaryData.Require(record >= records.Offset && record <= records.Offset + records.NominalBytes - 32,
                "LSMF stack record leaves the declared Stack storage.");
            BinaryData.Require(usedRecords.Add(record), "Aliased LSMF stack group record.");
            var members = access.Array(BinaryData.U64(bytes, record), BinaryData.U64(bytes, record + 8), 8);
            var amounts = access.Array(BinaryData.U64(bytes, record + 16), BinaryData.U64(bytes, record + 24), 8);
            BinaryData.Require(members.Count is > 0 and <= ushort.MaxValue && amounts.Count > 0,
                "Unsettled or empty LSMF stack group; quantities are unavailable.");
            var entityRows = new int[members.Count];
            var offsets = Enumerable.Range(0, members.Count).Select(_ => new List<int>()).ToArray();
            for (var index = 0; index < members.Count; index++)
            {
                entityRows[index] = access.Model.EntityRow(BinaryData.U64(bytes, members.Offset + index * 8));
                BinaryData.Require(usedMembers.Add(entityRows[index]), "A stack member belongs to multiple groups.");
            }
            for (var index = 0; index < amounts.Count; index++)
            {
                var offset = amounts.Offset + index * 8;
                _ = access.ColumnPointer((ulong)(offset - 48), entries, arenaRelative: true);
                BinaryData.Require(usedAmounts.Add(offset), "Aliased LSMF stack contribution.");
                var member = BinaryData.U16(bytes, offset);
                BinaryData.Require(member < members.Count && BinaryData.U32(bytes, offset + 4) > 0,
                    "Invalid LSMF stack member index or zero contribution.");
                offsets[member].Add(offset + 4); // Preserve the unknown u16 at +2 verbatim.
            }
            for (var member = 0; member < members.Count; member++)
            {
                // Missing contributions may represent a transient group; never invent a quantity of one.
                BinaryData.Require(offsets[member].Count > 0, "Unsettled LSMF stack member has no contribution.");
                var total = offsets[member].Sum(offset => (long)BinaryData.U32(access.Model.Bytes.Span, offset));
                result.Add(new LsmfStack(group, entityRows[member], offsets[member], total));
            }
        }
        return result;
    }

    internal static IReadOnlyList<LsmfSlot> Slots(LsmfAccess access)
    {
        var containers = access.Column(Inventory + "v1.ContainerComponent", 32);
        var slotData = access.Column(Inventory + "v0.ContainerSlotData", 16);
        if (containers is null || slotData is null) return [];
        BinaryData.Require(containers.Rows <= 100_000, "Too many LSMF containers.");
        var slots = new List<LsmfSlot>();
        var used = new HashSet<int>();
        var usedKeys = new HashSet<int>();
        var bytes = access.Model.Bytes.Span;
        for (var row = 0; row < containers.Rows; row++)
        {
            var offset = containers.Offset + row * 32;
            var keys = access.Array(BinaryData.U64(bytes, offset), BinaryData.U64(bytes, offset + 8), 2);
            var values = access.Array(BinaryData.U64(bytes, offset + 16), BinaryData.U64(bytes, offset + 24), 8);
            BinaryData.Require(keys.Count == values.Count && slots.Count <= 100_000 - keys.Count,
                "Unsupported LSMF container map or slot budget exceeded.");
            BinaryData.Require(keys.Count == 0 || !access.Model.Components.Any(column =>
                keys.Offset < column.Offset + column.NominalBytes && column.Offset < keys.Offset + keys.Count * 2),
                "Container keys overlap nominal component storage instead of a separate heap allocation.");
            var seenSlots = new HashSet<int>();
            var seenEntities = new HashSet<int>();
            for (var index = 0; index < values.Count; index++)
            {
                var pointer = BinaryData.U64(bytes, values.Offset + index * 8);
                var record = access.ColumnPointer(pointer, slotData, arenaRelative: true);
                BinaryData.Require(used.Add(record), "A container slot record is referenced more than once.");
                var entity = access.Model.EntityRow(BinaryData.U64(bytes, record));
                var slot = BinaryData.U32(bytes, record + 8);
                var keyOffset = keys.Offset + index * 2;
                var gridSlot = BinaryData.U16(bytes, keyOffset);
                BinaryData.Require(usedKeys.Add(keyOffset), "A container map key is aliased by another container.");
                BinaryData.Require(slot <= ushort.MaxValue && seenSlots.Add(gridSlot) && seenEntities.Add(entity),
                    "Ambiguous LSMF container keys or item references.");
                slots.Add(new LsmfSlot(row, index, entity, gridSlot, record + 8, keyOffset));
            }
        }
        return slots;
    }
}
