using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Exact observed container names select navigation groups, never guessed gameplay joins.</summary>
internal sealed class GvasNamedViews
{
    private const string Description = "Storage projection. Eligible scalar values reference canonical raw fields; actor observations and opaque values remain read-only. Group meanings are Inferred from serialized names by PS5 Save Game Toolkit contributors (GPL-3.0-or-later). Game effects, limits and prerequisites remain Unknown.";
    private readonly Dictionary<string, GvasPropertyCollection> _collections = new(StringComparer.Ordinal);
    private readonly List<Tab> _tabs = [];

    public GvasNamedViews(GvasFile file, UnrealGvasSchema schema, JediActorStream? actors, IReferenceContext references)
    {
        var entries = GvasPropertyEntry.Create(file.Properties, "property");
        // Image metadata captions belong to the schema overlay; its named projection keeps raw names.
        var catalogIds = UnrealGvasReferences.PropertyCatalogues(schema.Kind)
            .Where(id => id != UnrealGvasReferences.SackboyImageFields);
        var catalogs = catalogIds.Select(id => references.Catalog(id)).OfType<IReferenceCatalog>().ToArray();
        Func<GvasPropertyEntry, ViewNode>? scalarCell = schema.Kind == UnrealGvasSchemaKind.Generic
            ? null : GvasFieldReferenceFactory.Value;
        Add("gvas/properties", "All nested properties", entries, catalogs, valueCell: scalarCell);
        if (schema.Kind == UnrealGvasSchemaKind.SackboyGeneral)
        {
            Group("Progression", ["CollectabellSaveData", "AchievementLoadSaveData", "Pins", "Activities"], GvasFieldReferenceFactory.Value);
            Group("Costumes and emotes", ["CostumeSaveData", "unlockedEmotes", "SavedBoundEmotes"], GvasFieldReferenceFactory.Value);
            Group("World and levels", ["HostReplicatedData", "LevelData", "ZoneData", "StickerbookData"], GvasFieldReferenceFactory.Value);
            Group("Settings", ["Settings", "Tutorials"], GvasFieldReferenceFactory.Value);
        }
        else if (schema.Kind == UnrealGvasSchemaKind.JediSettings)
            _tabs.Add(new("Settings", [new TextNode(Description), Add("jedi/settings", "Display, input and saved overrides", entries, catalogs,
                valueCell: GvasFieldReferenceFactory.Value).View]));
        else if (schema.Kind == UnrealGvasSchemaKind.JediGameplay)
        {
            Group("Journey state", ["PlayerStartRecord", "InitialStreamingAreas", "MetaData", "TimeSaved"], GvasFieldReferenceFactory.Value);
            var children = new List<ViewNode> { new TextNode(Description) };
            if (actors is { IsValid: true })
            {
                children.Add(new TextNode("Actor tags and FString observations are separate indexes. Observed strings are not verified actor identities, collected states or editable fields. Offsets refer to the complete decoded stream, beginning with the ActorRecords FString length.") { Tone = Tone.Warning });
                var actorEntries = GvasPropertyEntry.Create(actors.Roots, "actors");
                if (!actors.CompleteTagParse) actorEntries = actorEntries.Select(entry => entry with
                    { EvidenceOverride = "Inferred tag scan; incomplete schema; game meaning Unknown" }).ToArray();
                const string actorDescription = "Read-only decoded actor stream. Ordinal row identities are local to this document; they are not proven persistent actor/gameplay identities or editable raw bindings. Captions: PS5 Save Game Toolkit contributors, GPL-3.0-or-later. String observations do not establish property boundaries or game meaning.";
                children.Add(Add("jedi/actor-tags", "Decoded actor properties", actorEntries, catalogs, actorDescription).View);
                children.Add(Add("jedi/actor-names", "Observed actor strings (bounded scan)", GvasPropertyEntry.Create(actors.ObservedNames, "actor-strings"), [], actorDescription).View);
            }
            else children.Add(new TextNode(actors?.Error ?? "No validated actor stream.") { Tone = Tone.Warning });
            _tabs.Add(new("Actors", children));
        }

        void Group(string title, IReadOnlyList<string> containers, Func<GvasPropertyEntry, ViewNode> valueCell)
        {
            var selected = entries.Where(entry => containers.Contains(entry.Node.Name, StringComparer.Ordinal) ||
                entry.Ancestors.Any(ancestor => containers.Contains(ancestor, StringComparer.Ordinal))).ToArray();
            if (selected.Length > 0)
                _tabs.Add(new(title, [new TextNode(Description), Add("gvas/group/" + title, title, selected, catalogs,
                    valueCell: valueCell).View]));
        }
    }

    public IReadOnlyList<QueryableCollection> Collections => _collections.Values.Select(collection => collection.Metadata).ToArray();
    public IReadOnlyList<Tab> Tabs => [new("Explore all properties", [_collections["gvas/properties"].View]), .. _tabs];
    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query) => _collections.TryGetValue(id, out var collection)
        ? collection.Query(query) : throw new KeyNotFoundException($"Unknown property collection '{id}'.");

    private GvasPropertyCollection Add(string id, string title, IReadOnlyList<GvasPropertyEntry> entries,
        IReadOnlyList<IReferenceCatalog> catalogs, string description = Description,
        Func<GvasPropertyEntry, ViewNode>? valueCell = null)
    {
        var collection = new GvasPropertyCollection(id, title, entries, catalogs, description, valueCell);
        _collections.Add(id, collection);
        return collection;
    }
}
