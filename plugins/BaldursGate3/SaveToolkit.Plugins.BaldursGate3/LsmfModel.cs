namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record LsmfComponent(int Index, string Name, ulong TypeHash, uint Flags,
    int ElementBytes, int Rows, int Offset, int NominalBytes);

internal sealed record LsmfEntity(string Guid, int Row, int Occurrence, int Copies)
{
    // Occurrence provenance prevents repeated GUIDs from becoming one invented entity.
    internal string Key => $"{Guid}/occurrence/{Occurrence}";
}

internal sealed class LsmfModel
{
    private readonly IReadOnlyDictionary<string, LsmfComponent> _componentsByName;
    private readonly Lazy<IReadOnlyList<LsmfEntity>> _entities;

    internal LsmfModel(ReadOnlyMemory<byte> bytes, int directoryOffset, IReadOnlyList<LsmfComponent> components)
    {
        Bytes = bytes;
        DirectoryOffset = directoryOffset;
        Components = components;
        _componentsByName = components.ToDictionary(component => component.Name, StringComparer.Ordinal);
        _entities = new(ReadEntities);
    }

    internal ReadOnlyMemory<byte> Bytes { get; }
    internal int DirectoryOffset { get; }
    internal IReadOnlyList<LsmfComponent> Components { get; }

    internal LsmfComponent? Component(string name) => _componentsByName.GetValueOrDefault(name);

    internal ReadOnlyMemory<byte> NominalData(LsmfComponent component) => Bytes.Slice(component.Offset, component.NominalBytes);

    /// <summary>Only EntityId has a validated fixed-row interpretation. No stats/owner joins are implied.</summary>
    internal IReadOnlyList<LsmfEntity> Entities() => _entities.Value;

    private IReadOnlyList<LsmfEntity> ReadEntities()
    {
        var component = Component(LsmfReader.EntityComponent);
        if (component is null) return [];
        const int guidBytes = 16;
        const int maxEntities = 100_000;
        BinaryData.Require(component.ElementBytes == guidBytes, "Unsupported LSMF EntityId element width.");
        BinaryData.Require(component.Rows <= maxEntities, "LSMF EntityId inspection exceeds the 100,000 occurrence budget.");
        var guids = new string[component.Rows];
        var copies = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var row = 0; row < guids.Length; row++)
        {
            // Larian's byte-order convention differs from System.Guid's ordinary display.
            var attribute = new ResourceAttribute("EntityId", LarianAttributeType.UUID,
                Bytes.Slice(component.Offset + row * guidBytes, guidBytes));
            var guid = AttributeDecoder.Read(attribute).AsString();
            guids[row] = guid;
            copies[guid] = copies.GetValueOrDefault(guid) + 1;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        return Array.AsReadOnly(guids.Select((guid, row) =>
        {
            var occurrence = seen.GetValueOrDefault(guid) + 1;
            seen[guid] = occurrence;
            return new LsmfEntity(guid, row, occurrence, copies[guid]);
        }).ToArray());
    }

    /// <summary>Explicit arena-relative pointer, never applied blindly to an unknown component.</summary>
    internal ReadOnlyMemory<byte> ArenaRange(ulong pointer, ulong length, int alignment = 1)
    {
        BinaryData.Require(alignment > 0 && pointer % (ulong)alignment == 0, "Unaligned LSMF arena pointer.");
        BinaryData.Require(pointer <= (ulong)(DirectoryOffset - LsmfReader.ArenaBase), "LSMF arena pointer is out of range.");
        var offset = (int)pointer + LsmfReader.ArenaBase;
        BinaryData.Require(length <= (ulong)(DirectoryOffset - offset), "LSMF arena range is out of bounds.");
        return Bytes.Slice(offset, (int)length);
    }

    /// <summary>Absolute EntityId pointer. Interior, sentinel and unrelated pointers are rejected.</summary>
    internal int EntityRow(ulong pointer)
    {
        var component = Component(LsmfReader.EntityComponent);
        BinaryData.Require(component is { ElementBytes: 16 }, "No supported LSMF EntityId table.");
        BinaryData.Require(pointer >= (ulong)component!.Offset && pointer < (ulong)component.Offset + (ulong)component.NominalBytes,
            "LSMF entity reference is outside EntityId.");
        var relative = pointer - (ulong)component.Offset;
        BinaryData.Require(relative % (ulong)component.ElementBytes == 0, "LSMF entity reference points inside a GUID.");
        return (int)(relative / (ulong)component.ElementBytes);
    }
}
