using SaveToolkit.Sdk;
using System.IO;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record LsmfInventoryEntry(int ContainerRow, int ContainerEntityRow, int ItemEntityRow,
    int Slot, int? OwnerBodyEntityRow, string Path, bool IsAmbiguous);

/// <summary>
/// Builds current container edges from explicit serialized maps. MemberData is not
/// used as a location signal because it is historical bookkeeping and may retain
/// stale rows. Traversal is cycle-safe and bounded for malformed saves.
/// </summary>
internal sealed class LsmfInventoryGraph
{
    private const int ContainerBytes = 32;
    private const int OwnedBytes = 8;
    private const int MaxDepth = 128;
    private readonly LsmfAccess _access;
    private readonly IReadOnlyDictionary<int, int> _containerByEntity;
    private readonly IReadOnlyDictionary<int, int?> _ownedByEntity;
    private readonly bool _hasGoldTemplates;

    internal LsmfInventoryGraph(LsmfAccess access, LsmfIdentityGraph identity, LarianResource? resource,
        IReadOnlyList<LsmfStack>? stacks = null)
    {
        _access = access;
        var containers = access.Rows("game.inventory.v1.ContainerComponent", ContainerBytes);
        _containerByEntity = containers.Where(row => row.EntityRow is not null)
            .GroupBy(row => row.EntityRow!.Value)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Row);
        _ownedByEntity = ReadOwners();
        var entries = LsmfInventory.Slots(access);
        Entries = BuildEntries(containers, entries, identity);
        _hasGoldTemplates = HasGoldTemplates(resource);
        Gold = LsmfGoldDiscovery.Read(access, resource, Entries, stacks ?? LsmfInventory.Stacks(access));
        Diagnostics = BuildDiagnostics();
    }

    internal IReadOnlyList<LsmfInventoryEntry> Entries { get; }
    internal IReadOnlyList<LsmfGoldCandidate> Gold { get; }
    internal IReadOnlyList<string> Diagnostics { get; }

    private IReadOnlyList<LsmfInventoryEntry> BuildEntries(IReadOnlyList<LsmfComponentRow> containers,
        IReadOnlyList<LsmfSlot> slots, LsmfIdentityGraph identity)
    {
        var containerEntities = containers.Where(row => row.EntityRow is not null)
            .ToDictionary(row => row.Row, row => row.EntityRow!.Value);
        var byContainer = slots.GroupBy(slot => slot.ContainerRow).ToDictionary(group => group.Key, group => group.ToArray());
        var itemContainers = slots.GroupBy(slot => slot.EntityRow).ToDictionary(group => group.Key, group => group.Select(slot => slot.ContainerRow).Distinct().ToArray());
        var output = new List<LsmfInventoryEntry>(slots.Count);
        var visitedContainers = new HashSet<int>();
        var bodyByOwner = identity.Party.Where(member => member.PrimaryInventoryEntityRow is not null)
            .SelectMany(member => new[] { (Owner: member.PartyEntityRow, Body: member.BodyEntityRow),
                (Owner: member.BodyEntityRow, Body: member.BodyEntityRow) })
            .ToDictionary(pair => pair.Owner, pair => pair.Body);
        var primary = identity.Party.Where(member => member.PrimaryInventoryEntityRow is not null)
            .SelectMany(member => member.PrimaryInventoryEntityRow is { } inventory && _containerByEntity.TryGetValue(inventory, out var row)
                ? [(row, member.BodyEntityRow)] : member.BodyEntityRow is { } body && _containerByEntity.TryGetValue(body, out var fallback)
                    ? [(fallback, member.BodyEntityRow)] : Array.Empty<(int, int)>());
        foreach (var root in primary.Distinct())
            Visit(root.Item1, root.Item2, "", new HashSet<int>(), 0);

        // IsOwnedComponent links every nested container to its current owner entity.
        // Follow that explicit chain so bags are attributed without using row order.
        foreach (var (entity, row) in _containerByEntity)
            if (TryOwnerBody(entity, bodyByOwner, out var ownerBody))
                Visit(row, ownerBody, "", new HashSet<int>(), 0);

        AppendUnownedEntries(output, slots, containerEntities, itemContainers);
        return output;

        void Visit(int containerRow, int ownerBody, string parent, HashSet<int> path, int depth)
        {
            if (depth > MaxDepth) throw new InvalidDataException($"Inventory nesting exceeds {MaxDepth} containers.");
            if (!visitedContainers.Add(containerRow) || !path.Add(containerRow) || !byContainer.TryGetValue(containerRow, out var rows) ||
                !containerEntities.TryGetValue(containerRow, out var containerEntity)) return;
            foreach (var slot in rows)
            {
                var ambiguous = itemContainers[slot.EntityRow].Length > 1;
                var pathId = string.IsNullOrEmpty(parent) ? $"container/{containerRow}/slot/{slot.Index}" :
                    $"{parent}/container/{containerRow}/slot/{slot.Index}";
                output.Add(new LsmfInventoryEntry(containerRow, containerEntity, slot.EntityRow, slot.Slot,
                    ownerBody, pathId, ambiguous));
                if (_containerByEntity.TryGetValue(slot.EntityRow, out var child))
                    Visit(child, ownerBody, pathId, path, depth + 1);
            }
            path.Remove(containerRow);
        }
    }

    private bool TryOwnerBody(int entity, IReadOnlyDictionary<int, int> ownerBodies, out int body)
    {
        var seen = new HashSet<int>();
        var current = entity;
        for (var depth = 0; depth <= MaxDepth; depth++)
        {
            if (ownerBodies.TryGetValue(current, out body)) return true;
            if (!seen.Add(current) || !_ownedByEntity.TryGetValue(current, out var owner) || owner is not { } next)
                break;
            current = next;
        }
        body = -1;
        return false;
    }

    private static void AppendUnownedEntries(List<LsmfInventoryEntry> output, IReadOnlyList<LsmfSlot> slots,
        IReadOnlyDictionary<int, int> containerEntities, IReadOnlyDictionary<int, int[]> itemContainers)
    {
        // Keep other explicit containers visible, but do not invent a party owner.
        var covered = output.Select(entry => (entry.ContainerRow, entry.ItemEntityRow)).ToHashSet();
        foreach (var slot in slots)
        {
            if (!containerEntities.ContainsKey(slot.ContainerRow) || !covered.Add((slot.ContainerRow, slot.EntityRow))) continue;
            var ambiguous = itemContainers[slot.EntityRow].Length > 1;
            output.Add(new LsmfInventoryEntry(slot.ContainerRow, containerEntities[slot.ContainerRow], slot.EntityRow,
                slot.Slot, null, $"container/{slot.ContainerRow}/slot/{slot.Index}", ambiguous));
        }
    }

    private IReadOnlyDictionary<int, int?> ReadOwners()
    {
        var rows = _access.Rows("game.inventory.v1.IsOwnedComponent", OwnedBytes);
        var bytes = _access.Model.Bytes.Span;
        var owners = new Dictionary<int, int?>();
        foreach (var row in rows)
        {
            if (row.EntityRow is not { } container) continue;
            var pointer = BinaryData.U64(bytes, row.Offset);
            try { owners[container] = _access.Model.EntityRow(pointer); }
            catch (SaveFormatException) { owners[container] = null; }
        }
        return owners;
    }

    private IReadOnlyList<string> BuildDiagnostics()
    {
        var diagnostics = new List<string>();
        if (Entries.Any(entry => entry.IsAmbiguous)) diagnostics.Add("Some current item rows occur in multiple containers; ownership is left ambiguous.");
        if (Gold.Count > 0) diagnostics.Add("Gold storage candidates include unowned rows and missing stack contributions; no authoritative player total or distribution is computed.");
        if (Gold.Count == 0 && _hasGoldTemplates)
            diagnostics.Add("Gold template occurrences have no unique LSF item-instance-to-Entity bridge; player ownership and distribution remain unavailable.");
        if (Entries.Count == 0 && _containerByEntity.Count > 0) diagnostics.Add("Container maps were present but no bounded current slot could be resolved.");
        return diagnostics;
    }

    private static bool HasGoldTemplates(LarianResource? resource)
    {
        if (resource is null) return false;
        return resource.Nodes.Where(node => node.Name == "Item").Any(node => node.Attributes
            .Select(index => resource.Attributes[index])
            .Where(attribute => attribute.Name == "Stats")
            .Select(attribute => AttributeDecoder.Read(attribute).ToString())
            .Any(LsmfGoldDiscovery.IsGold));
    }
}
