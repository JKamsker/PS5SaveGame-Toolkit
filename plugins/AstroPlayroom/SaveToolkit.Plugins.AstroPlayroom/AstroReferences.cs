using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal sealed class AstroReferences(IReferenceContext context, string fingerprint)
{
    public const string Collectibles = "astro_collectibles";
    public const string Worlds = "astro_worlds";
    public const string InspectActionId = "collectibles/inspect";
    private readonly IReferenceCatalog? _collectibles = BuildAgnostic(context.Catalog(Collectibles));
    private readonly IReferenceCatalog? _worlds = BuildAgnostic(context.Catalog(Worlds));

    public ReferenceRecord? Collectible(string group, string key) => Compatible(_collectibles?.Find($"{group}/{key}"));
    public ReferenceRecord? World(string path) => Compatible(_worlds?.Find(path));

    public bool Handles(string actionId) => actionId == InspectActionId;

    public ActionOutcome Inspect(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var catalog = _collectibles ?? throw new InvalidOperationException("The ASTRO catalogue pack is unavailable.");
        if (!parameters.TryGetValue("record", out var value) || value.Kind != FieldValueKind.Text ||
            Compatible(catalog.Find(value.AsString())) is not { } record)
        {
            throw new ArgumentException("Choose the existing ASTRO catalogue record.", nameof(parameters));
        }

        return new ActionOutcome
        {
            Message = $"{record.Name} · {record.Key} · {record.Category ?? "Uncategorised"}\n" +
                $"{record.Description}\n{record.AttributionText}\n" +
                "Inspection only; saved ownership and gameplay effects remain unknown. No bytes changed."
        };
    }

    private ReferenceRecord? Compatible(ReferenceRecord? record) =>
        record?.Attributes.TryGetValue("schemaFingerprint", out var schema) == true && schema == fingerprint ? record : null;

    // The samples carry no trusted game-build label. Do not pick build-specific data by guessing.
    private static IReferenceCatalog? BuildAgnostic(IReferenceCatalog? catalog) =>
        catalog?.Pack.GameBuilds.Count == 0 ? catalog : null;
}
