using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Exact observed container names select navigation groups, never guessed gameplay joins.</summary>
internal sealed class GvasNamedViews
{
    private const string Description = "Read-only storage projection. Group meanings are Inferred from serialized names by PS5 Save Game Toolkit contributors (GPL-3.0-or-later). Canonical raw identities refer to Properties/Search; game effects, limits and prerequisites remain Unknown.";
    private readonly Dictionary<string, GvasPropertyCollection> _collections = new(StringComparer.Ordinal);
    private readonly List<Tab> _tabs = [];

    public GvasNamedViews(GvasFile file, UnrealGvasSchema schema, JediActorStream? actors, IReferenceContext references)
    {
        var entries = GvasPropertyEntry.Create(file.Properties, "property");
        var catalogIds = new List<string> { UnrealGvasReferences.PropertyTypes };
        if (schema.Kind == UnrealGvasSchemaKind.SackboyGeneral)
        {
            catalogIds.Add(UnrealGvasReferences.SackboyFields);
            catalogIds.Add(UnrealGvasReferences.SackboyCostumes);
        }
        if (schema.Kind == UnrealGvasSchemaKind.JediGameplay) catalogIds.Add(UnrealGvasReferences.JediFields);
        if (schema.Kind == UnrealGvasSchemaKind.JediSettings) catalogIds.Add(UnrealGvasReferences.JediSettings);
        var catalogs = catalogIds.Select(id => references.Catalog(id)).OfType<IReferenceCatalog>().ToArray();
        Add("gvas/properties", "All nested properties", entries, catalogs);
        if (schema.Kind == UnrealGvasSchemaKind.SackboyGeneral)
        {
            Group("Progression", ["CollectabellSaveData", "AchievementLoadSaveData", "Pins", "Activities"]);
            Group("Costumes and emotes", ["CostumeSaveData", "unlockedEmotes", "SavedBoundEmotes"]);
            Group("World and levels", ["HostReplicatedData", "LevelData", "ZoneData", "StickerbookData"]);
            Group("Settings", ["Settings", "Tutorials"]);
        }
        else if (schema.Kind == UnrealGvasSchemaKind.JediSettings)
            _tabs.Add(new("Settings", [new TextNode(Description), Add("jedi/settings", "Display, input and saved overrides", entries, catalogs).View]));
        else if (schema.Kind == UnrealGvasSchemaKind.JediGameplay)
        {
            Group("Journey state", ["PlayerStartRecord", "InitialStreamingAreas", "MetaData", "TimeSaved"]);
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

        void Group(string title, IReadOnlyList<string> containers)
        {
            var selected = entries.Where(entry => containers.Contains(entry.Node.Name, StringComparer.Ordinal) ||
                entry.Ancestors.Any(ancestor => containers.Contains(ancestor, StringComparer.Ordinal))).ToArray();
            if (selected.Length > 0)
                _tabs.Add(new(title, [new TextNode(Description), Add("gvas/group/" + title, title, selected, catalogs).View]));
        }
    }

    public IReadOnlyList<QueryableCollection> Collections => _collections.Values.Select(collection => collection.Metadata).ToArray();
    public IReadOnlyList<Tab> Tabs => [new("Explore all properties", [_collections["gvas/properties"].View]), .. _tabs];
    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query) => _collections.TryGetValue(id, out var collection)
        ? collection.Query(query) : throw new KeyNotFoundException($"Unknown property collection '{id}'.");

    private GvasPropertyCollection Add(string id, string title, IReadOnlyList<GvasPropertyEntry> entries,
        IReadOnlyList<IReferenceCatalog> catalogs, string description = Description)
    {
        var collection = new GvasPropertyCollection(id, title, entries, catalogs, description);
        _collections.Add(id, collection);
        return collection;
    }
}
