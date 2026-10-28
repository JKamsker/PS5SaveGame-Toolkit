using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Bounded schema captions composed over canonical raw property identities.</summary>
internal sealed class GvasSchemaViews(UnrealGvasSchema schema, IReferenceContext references)
{
    private readonly UnrealGvasSchema _schema = schema;
    private readonly IReferenceContext _references = references;

    public IReadOnlyList<ViewNode> Build(GvasFile file, IReadOnlyDictionary<GvasNode, string> identities,
        Func<string, string, string, FieldNode> bindReference, SectionNode? actorView)
    {
        var nodes = GvasFile.Descendants(file.Properties).ToArray();
        var result = new List<ViewNode>
        {
            new StatsNode([
                new Stat("Schema", _schema.Name),
                new Stat("Build", _schema.Build),
                new Stat("Evidence", _schema.Confidence),
            ]),
            new TextNode(_schema.Evidence) { Tone = Tone.Info },
        };
        if (_schema.Kind == UnrealGvasSchemaKind.SackboyGeneral)
            result.Add(BuildNamedPropertyTable(nodes, UnrealGvasReferences.SackboyFields,
                ["CollectabellsStored", "TotalCollectabellsCollected", "UnlockedWorlds", "Checksum"],
                "Verified storage fields; checksum coverage and gameplay effects are unknown.", identities, bindReference));
        else if (_schema.Kind == UnrealGvasSchemaKind.SackboyImageSet)
            result.Add(BuildNamedPropertyTable(nodes, UnrealGvasReferences.SackboyImageFields,
                ["SurfaceWidth", "SurfaceHeight", "PixelFormat", "Image"],
                "Metadata is readable; PS5 channel order, swizzle and screenshot correspondence remain inferred.", identities, bindReference));
        else if (_schema.Kind == UnrealGvasSchemaKind.JediGameplay)
        {
            result.Add(BuildNamedPropertyTable(nodes, UnrealGvasReferences.JediFields,
                ["CompressedActorRecords", "UncompressedActorRecordsSize", "Crc32"],
                "Actor storage is read-only; scalar writes retain an unproven-checksum warning.", identities, bindReference));
            if (actorView is not null) result.Add(actorView);
        }
        else if (_schema.Kind == UnrealGvasSchemaKind.HogwartsMetadata)
            result.Add(new TextNode("Database tables are exposed in the Hogwarts tabs through the shared Kraken/SQLite codecs. Raw arrays and metadata remain read-only.") { Tone = Tone.Info });
        return result;
    }

    private TableNode BuildNamedPropertyTable(IReadOnlyList<GvasNode> nodes, string catalogId,
        IReadOnlyList<string> names, string description, IReadOnlyDictionary<GvasNode, string> identities,
        Func<string, string, string, FieldNode> bindReference)
    {
        var rows = names.SelectMany(name => nodes.Where(candidate => candidate.Name == name)
            .Cast<GvasNode?>().DefaultIfEmpty().Select(node =>
        {
            var id = node is null ? "undecoded/" + name : identities[node];
            var raw = node?.Scalar is { } scalar ? scalar.Value.ToString() ?? "" : node is null ? "Not decoded" : "Opaque";
            var record = _references.Catalog(catalogId)?.Find(name);
            var reference = bindReference($"schema/{catalogId}/{id}", catalogId, name)
            with
            {
                Confidence = record is null || node is null ? Confidence.Unknown : Confidence.Inferred,
                Description = record is null ? "Raw serialized property name; no pinned catalog record is available."
                    : record.Description + " " + record.AttributionText,
                Warning = "Read-only semantic overlay. The raw property remains the source of truth.",
            };
            return new TableRow(id, new Dictionary<string, ViewNode>
            {
                ["identity"] = new TextNode(id),
                ["property"] = reference,
                ["value"] = new TextNode(raw) { Monospace = node is null },
                ["type"] = new TextNode(node?.Type ?? "Undecoded") { Tone = node is null ? Tone.Warning : Tone.Normal },
            });
        })).ToArray();
        return new TableNode([new("identity", "Canonical raw identity"), new("property", "Catalog property"), new("type", "Storage type"), new("value", "Current value")], rows)
        {
            Title = "Observed schema fields",
            Description = description + " Catalog attribution is shown by each field.",
        };
    }

}
