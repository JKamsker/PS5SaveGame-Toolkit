namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Bounded, exact-type identifiers used by the save-local identity joins.</summary>
internal static class ResourceIdentifiers
{
    private const int MaxIdentifierBytes = 512;

    internal static string? Read(LarianResource resource, ResourceNode node, string name, LarianAttributeType type)
    {
        var values = node.Attributes.Select(index => resource.Attributes[index]).Where(attribute => attribute.Name == name).Take(2).ToArray();
        return values is [var attribute] && attribute.Type == type && attribute.Data.Length <= MaxIdentifierBytes
            ? AttributeDecoder.Read(attribute).AsString() : null;
    }
}
