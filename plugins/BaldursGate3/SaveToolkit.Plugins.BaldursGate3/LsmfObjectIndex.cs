namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Exact Creator Entity → typed TemplateID → CurrentTemplate label candidates only.</summary>
internal sealed class LsmfObjectIndex
{
    private readonly IReadOnlyDictionary<string, string> _labels;

    internal LsmfObjectIndex(LarianResource? resource)
    {
        if (resource is null) { _labels = new Dictionary<string, string>(); return; }
        var objects = resource.Nodes.Where(node => node.Name is "Item" or "Character")
            .Select(node => (Template: ResourceIdentifiers.Read(resource, node, "CurrentTemplate", LarianAttributeType.UUID),
                Label: node.Name == "Item" ? ResourceIdentifiers.Read(resource, node, "Stats", LarianAttributeType.FixedString) ?? "Item (unknown Stats)" : "Character body"))
            .Where(item => item.Template is not null).GroupBy(item => item.Template!, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Label).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Label, StringComparer.Ordinal);
        _labels = resource.Nodes.Where(node => node.Name == "Creator" && node.Parent >= 0 && resource.Nodes[node.Parent].Name == "Creators")
            .Select(node => (Entity: ResourceIdentifiers.Read(resource, node, "Entity", LarianAttributeType.UUID),
                Template: ResourceIdentifiers.Read(resource, node, "TemplateID", LarianAttributeType.UUID) ?? ResourceIdentifiers.Read(resource, node, "TemplateID", LarianAttributeType.FixedString)))
            .Where(item => item.Entity is not null && item.Template is not null && objects.ContainsKey(item.Template))
            .GroupBy(item => item.Entity!, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => objects[group.Single().Template!], StringComparer.Ordinal);
    }

    internal string? Find(string guid) => _labels.GetValueOrDefault(guid);


}
