using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Checked address conventions derived independently from the PS5 sample.</summary>
internal sealed class LsmfAccess
{
    internal const ulong Empty = ulong.MaxValue;
    internal LsmfModel Model { get; }
    private readonly Lazy<IReadOnlyDictionary<int, int[]>> _owners;

    internal LsmfAccess(LsmfModel model)
    {
        Model = model;
        _owners = new Lazy<IReadOnlyDictionary<int, int[]>>(ScanOwners);
    }

    internal LsmfComponent? Column(string name, int width)
    {
        var column = Model.Components.SingleOrDefault(c => c.Name == name);
        BinaryData.Require(column is null || column.ElementBytes == width, $"Unsupported {name} record width.");
        return column;
    }

    internal int Arena(ulong pointer, int length)
    {
        _ = Model.ArenaRange(pointer, (ulong)length);
        return checked((int)pointer + LsmfReader.ArenaBase);
    }

    internal (int Offset, int Count) Array(ulong begin, ulong end, int width, int maximum = 100_000)
    {
        if (begin == Empty && end == Empty) return (0, 0);
        BinaryData.Require(begin != Empty && end >= begin && (end - begin) % (ulong)width == 0,
            "Invalid LSMF array range.");
        var count = (end - begin) / (ulong)width;
        BinaryData.Require(count <= (ulong)maximum, "LSMF array exceeds its inspection budget.");
        return (Arena(begin, checked((int)count * width)), (int)count);
    }

    internal int ColumnPointer(ulong pointer, LsmfComponent column, bool arenaRelative)
    {
        BinaryData.Require(arenaRelative || pointer <= int.MaxValue, "LSMF absolute pointer is out of range.");
        var offset = arenaRelative ? Arena(pointer, column.ElementBytes) : (long)pointer;
        BinaryData.Require(offset >= column.Offset && offset < (long)column.Offset + column.NominalBytes &&
            (offset - column.Offset) % column.ElementBytes == 0, "LSMF pointer is outside its declared component rows.");
        return (int)offset;
    }

    internal string String(ulong pointer, uint length)
    {
        BinaryData.Require(length is > 0 and <= 512, "Invalid LSMF string length.");
        var offset = Arena(pointer, (int)length);
        var bytes = Model.Bytes.Span.Slice(offset, (int)length);
        foreach (var value in bytes)
            BinaryData.Require(value is >= 0x20 and <= 0x7e, "Unsupported non-ASCII LSMF identifier.");
        return System.Text.Encoding.ASCII.GetString(bytes);
    }

    internal IReadOnlyDictionary<int, int[]> Owners() => _owners.Value;

    private IReadOnlyDictionary<int, int[]> ScanOwners()
    {
        var entities = Column(LsmfReader.EntityComponent, 16);
        if (entities is null) return new Dictionary<int, int[]>();
        var found = new Dictionary<int, int[]>();
        var ambiguous = new HashSet<int>();
        var totalOwners = 0;
        var bytes = Model.Bytes.Span;
        // Owner descriptors in this variant are aligned to 32 absolute bytes. Only exact
        // bounded row-count/range matches are candidates; duplicate candidates are not joined.
        for (var offset = 64; offset <= Model.DirectoryOffset - 32; offset += 32)
        {
            var start = BinaryData.U64(bytes, offset);
            var end = BinaryData.U64(bytes, offset + 8);
            var component = BinaryData.U64(bytes, offset + 16);
            var count = BinaryData.U64(bytes, offset + 24);
            if (component >= (ulong)Model.Components.Count || count == 0 || count > 100_000 ||
                count != (ulong)Model.Components[(int)component].Rows || start < 48 || start % 4 != 0 ||
                end < start || end > (ulong)Model.DirectoryOffset || end - start != count * 4) continue;
            if (ambiguous.Contains((int)component)) continue;
            if (found.ContainsKey((int)component)) { ambiguous.Add((int)component); continue; }
            BinaryData.Require(totalOwners <= 1_000_000 - (int)count, "LSMF ownerlist inspection budget exceeded.");
            // Charge malformed candidates too: repeated invalid descriptors must not
            // cause unbounded array allocations or repeated full owner-array scans.
            totalOwners += (int)count;
            var owners = new int[(int)count];
            var valid = true;
            for (var row = 0; row < owners.Length; row++)
            {
                var owner = BinaryData.U32(bytes, (int)start + row * 4);
                if (owner >= entities.Rows) { valid = false; break; }
                owners[row] = (int)owner;
            }
            if (!valid) continue;
            found.Add((int)component, owners);
        }
        foreach (var component in ambiguous) found.Remove(component);
        return found;
    }

    /// <summary>Returns component rows with their owner entity when an ownerlist exists.</summary>
    internal IReadOnlyList<LsmfComponentRow> Rows(string name, int width)
    {
        var component = Column(name, width);
        if (component is null) return [];
        var owners = Owners();
        if (!owners.TryGetValue(component.Index, out var ownerRows))
            return Enumerable.Range(0, component.Rows).Select(row => new LsmfComponentRow(component, row, null)).ToArray();
        BinaryData.Require(ownerRows.Length == component.Rows, $"Ownerlist length does not match {name}.");
        return ownerRows.Select((entity, row) => new LsmfComponentRow(component, row, entity)).ToArray();
    }

    internal int? RowForEntity(string name, int width, int entityRow)
    {
        var matches = RowsForEntity(name, width, entityRow);
        return matches.Length == 1 ? matches[0] : null;
    }

    internal int[] RowsForEntity(string name, int width, int entityRow) => Rows(name, width)
        .Where(candidate => candidate.EntityRow == entityRow).Select(candidate => candidate.Row).Distinct().ToArray();

    internal static string Guid(ReadOnlyMemory<byte> bytes) => AttributeDecoder.Read(
        new ResourceAttribute("GUID", LarianAttributeType.UUID, bytes)).AsString();
}

internal sealed record LsmfComponentRow(LsmfComponent Component, int Row, int? EntityRow)
{
    internal int Offset => Component.Offset + checked(Row * Component.ElementBytes);
}
