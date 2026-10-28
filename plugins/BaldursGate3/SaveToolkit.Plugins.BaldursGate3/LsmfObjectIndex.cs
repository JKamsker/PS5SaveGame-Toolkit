namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Exact Creator Entity → typed TemplateID → CurrentTemplate label candidates only.</summary>
internal sealed class LsmfObjectIndex
{
    private readonly IReadOnlyDictionary<string, string> _labels;

    internal LsmfObjectIndex(LarianResource? resource)
    {
        if (resource is null) { _labels = new Dictionary<string, string>(); return; }
        var objects = resource.Nodes.Where(node => node.Name is "Item" or "Character")
            .Select(node => (Template: Read(resource, node, "CurrentTemplate", LarianAttributeType.UUID),
                Label: node.Name == "Item" ? Read(resource, node, "Stats", LarianAttributeType.FixedString) ?? "Item (unknown Stats)" : "Character body"))
            .Where(item => item.Template is not null).GroupBy(item => item.Template!, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Label).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Label, StringComparer.Ordinal);
        _labels = resource.Nodes.Where(node => node.Name == "Creator" && node.Parent >= 0 && resource.Nodes[node.Parent].Name == "Creators")
            .Select(node => (Entity: Read(resource, node, "Entity", LarianAttributeType.UUID),
                Template: Read(resource, node, "TemplateID", LarianAttributeType.UUID) ?? Read(resource, node, "TemplateID", LarianAttributeType.FixedString)))
            .Where(item => item.Entity is not null && item.Template is not null && objects.ContainsKey(item.Template))
            .GroupBy(item => item.Entity!, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => objects[group.Single().Template!], StringComparer.Ordinal);
    }

    internal string? Find(string guid) => _labels.GetValueOrDefault(guid);

    private static string? Read(LarianResource resource, ResourceNode node, string name, LarianAttributeType type)
    {
        var values = node.Attributes.Select(index => resource.Attributes[index]).Where(a => a.Name == name).Take(2).ToArray();
        return values is [var attribute] && attribute.Type == type && attribute.Data.Length <= 512
            ? AttributeDecoder.Read(attribute).AsString() : null;
    }
}
