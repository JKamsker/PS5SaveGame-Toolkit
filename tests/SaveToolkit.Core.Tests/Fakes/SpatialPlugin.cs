using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Synthetic F9 example: original CC0 schematic, save-derived markers and a link-only region.</summary>
public sealed class SpatialPlugin : IGamePlugin
{
    public const string TitleId = "PPSA99993";
    public const string AssetId = "test.spatial:schematic.png";
    public const string RepositoryUrl = "https://github.com/JKamsker/PS5SaveGame-Toolkit";
    public static ReferenceAttribution OriginalAttribution { get; } = new(RepositoryUrl,
        "PS5 Save Game Toolkit contributors", "CC0-1.0", "https://creativecommons.org/publicdomain/zero/1.0/",
        new DateOnly(2026, 10, 28), "Original synthetic schematic and coordinates; no game or community assets.");

    public PluginInfo Info { get; } = new("test.spatial", "Spatial view test format", "1.0.0")
    {
        Games = [new GameTitle(TitleId, "Spatial Test Game")],
        References = [RepositoryUrl],
    };

    public static byte[] Create() => [.. "SPAT"u8, 0, 0];

    public ProbeResult Probe(ISaveFile file) => file.ReadHeader(4).AsSpan().SequenceEqual("SPAT"u8)
        ? ProbeResult.Match(ProbeScore.Title, "Spatial example") : ProbeResult.None;

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        var bytes = file.ReadAllBytes();
        if (bytes.Length != 6 || !bytes.AsSpan(0, 4).SequenceEqual("SPAT"u8)) throw new SaveFormatException("Not a spatial test save.");
        return new SpatialDocument(file, bytes, references);
    }

    private sealed class SpatialDocument(ISaveFile file, byte[] bytes, IReferenceContext references) : BoundSaveDocument
    {
        public override string Title => "Spatial example";

        public override ViewNode BuildView() => new SectionNode("Spatial example",
        [
            Graph(), Map(), ExternalRegion(),
            new SectionNode("Controls",
            [
                Field("skill/dash", "Dash unlocked", new BoolSpec(), () => FieldValue.Of(bytes[4] != 0), v => bytes[4] = v.AsBool() ? (byte)1 : (byte)0),
                Field("world/cache", "Cache found", new BoolSpec(), () => FieldValue.Of(bytes[5] != 0), v => bytes[5] = v.AsBool() ? (byte)1 : (byte)0),
                new ActionNode("reset", "Reset discoveries"),
            ]) { Collapsed = true },
        ]);

        private GraphNode Graph() => new("Skills",
        [
            new GraphItem("walk", "Walk", GraphState.Unlocked) { Branch = "Movement" },
            new GraphItem("dash", "Dash", bytes[4] == 0 ? GraphState.Available : GraphState.Unlocked)
                { Cost = "2 points", Branch = "Movement", Target = Control(ViewTargetKind.Field, "skill/dash") },
            new GraphItem("glide", "Glide", GraphState.Locked) { Cost = "4 points", Branch = "Movement" },
            new GraphItem("respec", "Respec", GraphState.Unknown) { Target = Control(ViewTargetKind.Action, "reset") },
        ], [new GraphEdge("walk", "dash"), new GraphEdge("dash", "glide")],
            "Walk is the prerequisite for Dash; Dash is the prerequisite for Glide. Respec links to reset controls.");

        private MapNode Map()
        {
            var asset = references.GetAsset(AssetId);
            var provenance = references.AssetProvenance(AssetId);
            return new MapNode("Original schematic", new MapBounds(0, 0, 100, 70),
            [
                new MapMarker("cache", "Courtyard cache", bytes[5] == 0 ? MapMarkerStatus.Missing : MapMarkerStatus.Found, "Caches")
                    { X = 20, Y = 40, Confidence = Confidence.Verified, Target = Control(ViewTargetKind.Field, "world/cache"),
                        Description = "Original synthetic location, not a coordinate inferred from a game map." },
                new MapMarker("gate", "East gate", MapMarkerStatus.Unknown, "Travel") { X = 80, Y = 40, Confidence = Confidence.Unknown },
            ], "The courtyard cache is west of the east gate. Unknown does not mean missing.")
            {
                CoordinateSpace = "Original schematic, 100 × 70 units, north up",
                Transform = new MapTransform(1, -1, 0, 70),
                CoordinateAttribution = OriginalAttribution,
                CoordinateEmbeddingPermitted = true,
                Background = asset is null || provenance is null ? null
                    : new MapBackground(AssetId, "Original courtyard and east-gate schematic", provenance.Attribution, EmbeddingPermitted: true),
            };
        }

        private static MapNode ExternalRegion() => new("Community region (external only)", new MapBounds(0, 0, 1, 1),
        [
            new MapMarker("region", "Unknown region", MapMarkerStatus.Unknown, "Region")
            {
                ExternalLink = new ExternalViewLink("https://example.org/community-map#region", "Open region guide",
                    "Example community; licence unverified. Link only, no art or coordinates imported."),
            },
        ], "Region details are available only at the external source. No coordinates or artwork have been copied.")
        {
            ExternalSource = new ExternalViewLink("https://example.org/community-map", "Open community map",
                "Example community; licence unverified. This fixture does not contact the site."),
        };

        private static ViewTarget Control(ViewTargetKind kind, string id) => new(kind, id)
            { Path = ["section:Spatial example", "section:Controls"] };

        public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
        {
            var result = base.ApplyEdits(edits);
            return result with { StructureChanged = result.Succeeded && result.Changes.Count > 0 };
        }

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
            actionId == "reset" ? new ActionOutcome
            {
                Changes = SetValues([new ValueEdit("skill/dash", FieldValue.Of(false)), new ValueEdit("world/cache", FieldValue.Of(false))]),
                StructureChanged = true,
            } : base.InvokeAction(actionId, parameters);

        public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput(file.RelativePath, bytes.ToArray())];
    }
}
