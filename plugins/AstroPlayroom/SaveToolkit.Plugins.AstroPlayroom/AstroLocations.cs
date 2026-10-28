using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.AstroPlayroom;

/// <summary>Schema-qualified candidate joins, shared by navigation and ownership presets.</summary>
internal sealed class AstroLocations
{
    private readonly AstroReferences _references;
    private readonly IReadOnlyDictionary<string, ReferenceRecord?> _records;

    public AstroLocations(AstroSchema schema, AstroReferences references)
    {
        _references = references;
        _records = schema.Records.GroupBy(Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Single())
            .ToDictionary(Key, record => references.Collectible(record.Group, record.Element.Name), StringComparer.Ordinal);
    }

    public ReferenceRecord? Reference(AstroCollectionRecord record) =>
        _records.GetValueOrDefault(Key(record));

    public string? WorldKey(AstroCollectionRecord record)
    {
        var reference = Reference(record);
        if (reference?.Attributes.GetValueOrDefault("worldKey") is { } world) return world;
        var stage = reference?.Attributes.GetValueOrDefault("stageKey");
        if (stage is null) return null;
        var split = stage.LastIndexOf('/');
        return split > "World/".Length ? stage[..split] : stage;
    }

    public ReferenceRecord? World(AstroCollectionRecord record) =>
        WorldKey(record) is { } key ? _references.World(key) : null;

    public static string Key(AstroCollectionRecord record) => $"{record.Group}/{record.Element.Name}";
}
