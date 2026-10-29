namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record LsmfGoldCandidate(string Guid, IReadOnlyList<int> EntityRows,
    int? ContainerRow, int? OwnerBodyEntityRow, LsmfStack? Stack, bool IsAmbiguous);

/// <summary>
/// Classifies gold only when every saved Item using the Creator template has gold Stats.
/// Repeated EntityId occurrences are retained: a unique stack and a unique slot may use
/// different occurrences of the same persistent GUID. No occurrence is chosen by proximity.
/// This establishes a storage candidate, never an authoritative party balance.
/// Independently measured PS5 facts; see FIELD_DISCOVERY.md (2026-10-29).
/// </summary>
internal static class LsmfGoldDiscovery
{
    internal static IReadOnlyList<LsmfGoldCandidate> Read(LsmfAccess access, LarianResource? resource,
        IReadOnlyList<LsmfInventoryEntry> inventory, IReadOnlyList<LsmfStack> stacks)
    {
        if (resource is null) return [];
        var goldTemplates = resource.Nodes.Where(node => node.Name == "Item")
            .Select(node => (Template: ResourceIdentifiers.Read(resource, node, "CurrentTemplate", LarianAttributeType.UUID),
                Stats: ResourceIdentifiers.Read(resource, node, "Stats", LarianAttributeType.FixedString)))
            .Where(item => item.Template is not null)
            .GroupBy(item => item.Template!, StringComparer.Ordinal)
            .Where(group => group.All(item => IsGold(item.Stats)))
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        if (goldTemplates.Count == 0) return [];

        var goldGuids = resource.Nodes.Where(node => node.Name == "Creator" && node.Parent >= 0 &&
                resource.Nodes[node.Parent].Name == "Creators")
            .Select(node => (Entity: ResourceIdentifiers.Read(resource, node, "Entity", LarianAttributeType.UUID),
                Template: ResourceIdentifiers.Read(resource, node, "TemplateID", LarianAttributeType.UUID) ??
                    ResourceIdentifiers.Read(resource, node, "TemplateID", LarianAttributeType.FixedString)))
            .Where(creator => creator.Entity is not null)
            .GroupBy(creator => creator.Entity!, StringComparer.Ordinal)
            .Where(group => group.All(creator => creator.Template is not null) &&
                group.Select(creator => creator.Template).Distinct(StringComparer.Ordinal).Count() == 1 &&
                goldTemplates.Contains(group.First().Template!))
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        if (goldGuids.Count == 0) return [];

        var entities = access.Model.Entities();
        var slotsByGuid = inventory.GroupBy(entry => entities[entry.ItemEntityRow].Guid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var stacksByGuid = stacks.GroupBy(stack => entities[stack.EntityRow].Guid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return entities.Where(entity => goldGuids.Contains(entity.Guid)).GroupBy(entity => entity.Guid, StringComparer.Ordinal)
            .Select(group =>
            {
                var slots = slotsByGuid.GetValueOrDefault(group.Key, []);
                var contributions = stacksByGuid.GetValueOrDefault(group.Key, []);
                var slot = slots is [var singleSlot] ? singleSlot : null;
                var stack = contributions is [var singleStack] ? singleStack : null;
                return new LsmfGoldCandidate(group.Key, group.Select(entity => entity.Row).ToArray(),
                    slot?.ContainerRow, slot?.OwnerBodyEntityRow, stack,
                    slot is null || slot.IsAmbiguous || stack is null);
            }).ToArray();
    }

    internal static bool IsGold(string? stats) => stats is not null &&
        (stats.Equals("OBJ_GoldCoin", StringComparison.OrdinalIgnoreCase) ||
         stats.Equals("OBJ_GoldPile", StringComparison.OrdinalIgnoreCase));
}
