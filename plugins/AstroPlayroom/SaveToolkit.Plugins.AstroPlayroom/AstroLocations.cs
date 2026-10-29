using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.AstroPlayroom;

/// <summary>Schema-qualified candidate joins, shared by navigation and ownership presets.</summary>
internal sealed class AstroLocations
{
    private readonly IReadOnlyDictionary<string, Location> _locations;
    private readonly IReadOnlySet<string> _mappedWorldKeys;

    public AstroLocations(AstroSchema schema, AstroReferences references)
    {
        var records = schema.Records.GroupBy(Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Single())
            .ToDictionary(Key, record => references.Collectible(record.Group, record.Element.Name), StringComparer.Ordinal);
        _locations = schema.Records.ToDictionary(record => record.Element.Id, record =>
        {
            var reference = records.GetValueOrDefault(record.Key);
            var worldKey = ResolveWorldKey(reference);
            return new Location(reference, worldKey, worldKey is null ? null : references.World(worldKey));
        }, StringComparer.Ordinal);
        _mappedWorldKeys = schema.Records.Select(WorldKey).OfType<string>().ToHashSet(StringComparer.Ordinal);
    }

    public ReferenceRecord? Reference(AstroCollectionRecord record) =>
        _locations[record.Element.Id].Reference;

    public string? WorldKey(AstroCollectionRecord record) => _locations[record.Element.Id].WorldKey;

    private static string? ResolveWorldKey(ReferenceRecord? reference)
    {
        if (reference?.Attributes.GetValueOrDefault("worldKey") is { } world) return world;
        var stage = reference?.Attributes.GetValueOrDefault("stageKey");
        if (stage is null) return null;
        var split = stage.LastIndexOf('/');
        return split > "World/".Length ? stage[..split] : stage;
    }

    public ReferenceRecord? World(AstroCollectionRecord record) =>
        _locations[record.Element.Id].World;

    public ReferenceEligibility AssessWorld(ReferenceRecord candidate) =>
        _mappedWorldKeys.Contains(candidate.Key)
            ? new ReferenceEligibility()
            : new ReferenceEligibility { Eligible = false, Reasons = ["No existing saved collectible has a mapped record in this candidate world."] };

    public static string Key(AstroCollectionRecord record) => record.Key;

    private sealed record Location(ReferenceRecord? Reference, string? WorldKey, ReferenceRecord? World);
}
