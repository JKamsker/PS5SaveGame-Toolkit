using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.AstroPlayroom;

/// <summary>Schema-qualified candidate joins, shared by navigation and ownership presets.</summary>
internal sealed class AstroLocations
{
    private readonly AstroReferences _references;
    private readonly IReadOnlyDictionary<string, ReferenceRecord?> _records;
    private readonly IReadOnlySet<string> _mappedWorldKeys;

    public AstroLocations(AstroSchema schema, AstroReferences references)
    {
        _references = references;
        _records = schema.Records.GroupBy(Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Single())
            .ToDictionary(Key, record => references.Collectible(record.Group, record.Element.Name), StringComparer.Ordinal);
        _mappedWorldKeys = schema.Records.Select(WorldKey).OfType<string>().ToHashSet(StringComparer.Ordinal);
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

    public ReferenceEligibility AssessWorld(ReferenceRecord candidate) =>
        _mappedWorldKeys.Contains(candidate.Key)
            ? new ReferenceEligibility()
            : new ReferenceEligibility { Eligible = false, Reasons = ["No existing saved collectible has a mapped record in this candidate world."] };

    public static string Key(AstroCollectionRecord record) => $"{record.Group}/{record.Element.Name}";
}
