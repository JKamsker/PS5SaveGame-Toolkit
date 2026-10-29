using SaveToolkit.Sdk;
using System.IO;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record LsmfInventoryEntry(int ContainerRow, int ContainerEntityRow, int ItemEntityRow,
    int Slot, int? OwnerBodyEntityRow, string Path, bool IsAmbiguous);

internal sealed record LsmfGoldCandidate(int ItemEntityRow, int? ContainerRow, int? OwnerBodyEntityRow,
    string Stats, bool IsAmbiguous);

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
    private readonly IReadOnlyDictionary<string, int[]> _itemRowsByStats;
    private readonly bool _hasGoldTemplates;

    internal LsmfInventoryGraph(LsmfAccess access, LsmfIdentityGraph identity, LarianResource? resource)
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
        _itemRowsByStats = ReadItems(resource);
        _hasGoldTemplates = HasGoldTemplates(resource);
        Gold = BuildGold();
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

        // Keep other explicit containers visible, but do not invent a party owner.
        var covered = output.Select(entry => (entry.ContainerRow, entry.ItemEntityRow)).ToHashSet();
        foreach (var slot in slots)
        {
            if (!containerEntities.ContainsKey(slot.ContainerRow) || !covered.Add((slot.ContainerRow, slot.EntityRow))) continue;
            var ambiguous = itemContainers[slot.EntityRow].Length > 1;
            output.Add(new LsmfInventoryEntry(slot.ContainerRow, containerEntities[slot.ContainerRow], slot.EntityRow,
                slot.Slot, null, $"container/{slot.ContainerRow}/slot/{slot.Index}", ambiguous));
        }
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

        bool TryOwnerBody(int entity, IReadOnlyDictionary<int, int> ownerBodies, out int body)
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

    private IReadOnlyDictionary<string, int[]> ReadItems(LarianResource? resource)
    {
        if (resource is null) return new Dictionary<string, int[]>(StringComparer.Ordinal);
        var entities = _access.Model.Entities();
        var creatorTemplates = resource.Nodes.Where(node => node.Name == "Creator" && node.Parent >= 0 &&
                resource.Nodes[node.Parent].Name == "Creators")
            .Select(node => (Entity: ResourceIdentifiers.Read(resource, node, "Entity", LarianAttributeType.UUID),
                Template: ResourceIdentifiers.Read(resource, node, "TemplateID", LarianAttributeType.UUID) ??
                    ResourceIdentifiers.Read(resource, node, "TemplateID", LarianAttributeType.FixedString)))
            .Where(pair => pair.Entity is not null && pair.Template is not null)
            .ToDictionary(pair => pair.Entity!, pair => pair.Template!, StringComparer.Ordinal);
        var entityByTemplate = creatorTemplates.GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Key, StringComparer.Ordinal);
        var items = resource.Nodes.Where(node => node.Name == "Item")
            .Select(node => (Template: ResourceIdentifiers.Read(resource, node, "CurrentTemplate", LarianAttributeType.UUID),
                Stats: ResourceIdentifiers.Read(resource, node, "Stats", LarianAttributeType.FixedString)))
            .Where(item => item.Template is not null && item.Stats is not null && entityByTemplate.ContainsKey(item.Template!))
            .ToArray();
        var wantedGuids = items.Select(item => entityByTemplate[item.Template!]).ToHashSet(StringComparer.Ordinal);
        var byGuid = entities.Where(entity => wantedGuids.Contains(entity.Guid))
            .GroupBy(entity => entity.Guid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entity => entity.Row).ToArray(), StringComparer.Ordinal);
        return items
            .SelectMany(item => byGuid.GetValueOrDefault(entityByTemplate[item.Template!], [])
                .Select(row => (Stats: item.Stats!, Row: row)))
            .GroupBy(item => item.Stats, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Row).Distinct().ToArray(), StringComparer.Ordinal);
    }

    private IReadOnlyList<LsmfGoldCandidate> BuildGold()
    {
        var goldRows = _itemRowsByStats.Where(pair => IsGoldStats(pair.Key))
            .SelectMany(pair => pair.Value.Select(row => (Stats: pair.Key, Row: row)));
        return goldRows.Select(item =>
        {
            var matches = Entries.Where(entry => entry.ItemEntityRow == item.Row).ToArray();
            var owner = matches.Select(entry => entry.OwnerBodyEntityRow).Distinct().ToArray();
            return new LsmfGoldCandidate(item.Row, matches.Length == 1 ? matches[0].ContainerRow : null,
                owner.Length == 1 ? owner[0] : null, item.Stats,
                matches.Length != 1 || owner.Length != 1 || matches.Any(entry => entry.IsAmbiguous));
        }).ToArray();
    }

    private IReadOnlyList<string> BuildDiagnostics()
    {
        var diagnostics = new List<string>();
        if (Entries.Any(entry => entry.IsAmbiguous)) diagnostics.Add("Some current item rows occur in multiple containers; ownership is left ambiguous.");
        if (Gold.Any(candidate => candidate.IsAmbiguous)) diagnostics.Add("Gold candidates include world/trader or multiply referenced rows; no player total is computed.");
        if (Gold.Count == 0 && (_hasGoldTemplates || _itemRowsByStats.Keys.Any(IsGoldStats)))
            diagnostics.Add("Gold template occurrences have no unique LSF item-instance-to-Entity bridge; player ownership and distribution remain unavailable.");
        if (Entries.Count == 0 && _containerByEntity.Count > 0) diagnostics.Add("Container maps were present but no bounded current slot could be resolved.");
        return diagnostics;
    }

    private static bool IsGoldStats(string stats) => stats.Equals("OBJ_GoldCoin", StringComparison.OrdinalIgnoreCase) ||
        stats.Equals("OBJ_GoldPile", StringComparison.OrdinalIgnoreCase) ||
        stats.Equals("OBJ_Goldpile", StringComparison.OrdinalIgnoreCase);

    private static bool HasGoldTemplates(LarianResource? resource)
    {
        if (resource is null) return false;
        return resource.Nodes.Where(node => node.Name == "Item").Any(node => node.Attributes
            .Select(index => resource.Attributes[index])
            .Where(attribute => attribute.Name == "Stats")
            .Select(attribute => AttributeDecoder.Read(attribute).ToString())
            .Any(IsGoldStats));
    }


}
