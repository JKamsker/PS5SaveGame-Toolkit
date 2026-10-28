using System.Globalization;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

/// <summary>Names enrich inspection only. Catalog categories and limits never authorize save writes.</summary>
internal sealed class CyberpunkReferences
{
    public const string Upstream = "https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2";
    public const string BaselineAttribution = "DeweySalt / Deweh, CyberCAT-SimpleGUI, MIT; retrieved 2026-10-28. " +
        Upstream + "/items.bin; categories researched using " + Upstream + "/CP2077SaveEditor/Resources/ItemClasses.json. " +
        "Internal identifier only; no localization, availability or game-limit claim.";
    private const string ActionPrefix = "inspect-catalog/";
    private const string IdentifierPackId = "cyberpunk2077.identifiers";
    private static readonly string[] CatalogIds = ["items", "facts", "vehicles", "travel", "item-labels"];
    private readonly IReferenceContext _context;
    private readonly IReadOnlyDictionary<string, IReferenceCatalog?> _catalogs;

    public CyberpunkReferences(IReferenceContext context, int gameVersion)
    {
        _context = context;
        var build = gameVersion.ToString(CultureInfo.InvariantCulture);
        _catalogs = CatalogIds
            .ToDictionary(id => id, id => context.Catalog(id, build), StringComparer.Ordinal);
    }

    public IReferenceCatalog? Items => _catalogs["items"];
    public IReferenceCatalog? Facts => _catalogs["facts"];
    public IReferenceCatalog? Catalog(string id) => _catalogs.GetValueOrDefault(id);
    public ReferenceRecord? ItemLabel(ulong id)
    {
        // A selected same-build/local item pack owns its name, description and icon together.
        if (Items is { } catalog && catalog.Pack.Id != IdentifierPackId && Item(id) is not null) return null;
        return _catalogs["item-labels"]?.Find(ItemKey(id));
    }
    public string? ItemDisplayName(ulong id) => ItemLabel(id)?.Name ?? Item(id)?.Name;
    public string ItemAttribution(ulong id) => string.Join("\n", new[] { Item(id)?.AttributionText, ItemLabel(id)?.AttributionText }.OfType<string>());

    public static IEnumerable<ReferenceRecord> All(IReferenceCatalog? catalog)
    {
        if (catalog is null) yield break;
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
            foreach (var record in catalog.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
                yield return record;
    }
    public static string ItemKey(ulong id) => id.ToString(CultureInfo.InvariantCulture);
    public ReferenceRecord? Item(ulong id) => Items?.Find(ItemKey(id));
    public ReferenceRecord? Fact(uint hash)
    {
        var record = Facts?.Find(hash.ToString(CultureInfo.InvariantCulture));
        var canonical = record?.Attributes.GetValueOrDefault("canonicalName") ?? record?.Name;
        return canonical is not null && FactHash(canonical) == hash ? record : null;
    }

    public AssetContent? GetAsset(string id) => _context.GetAsset(id);

    public string? AssetAttribution(string id) => _context.AssetProvenance(id)?.Attribution.ToShortText();

    public IReadOnlyList<ViewNode> Build(bool allowActions) => [
        new TextNode("Catalogs describe identifiers, not saved ownership, unlocks, completion or safe replacements. " +
            "Community build/DLC coverage is unknown. A small pinned CC BY-SA wiki pack supplies base-record names; " +
            "same-build localization and icons require a separately attributed local pack."),
        .. _catalogs.Select(pair => (ViewNode)new SectionNode(pair.Key, pair.Value is { } catalog
            ? [new TextNode($"{catalog.Count} records · {catalog.Pack.Id} {catalog.Pack.Version}"),
                new TextNode(catalog.Dataset.Attribution.ToShortText()),
                .. (allowActions && catalog.Count > 0 ? new ViewNode[] { CatalogAction(catalog) }
                    : [new TextNode("Catalog inspection actions require an editable document in the current host. Names in read-only views remain available.")])]
            : [new TextNode("No matching reference pack. Raw IDs and the reviewed baseline item dictionary remain available.")]))];

    public bool IsInspection(string actionId) => actionId.StartsWith(ActionPrefix, StringComparison.Ordinal);

    public ActionOutcome Inspect(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var id = actionId[ActionPrefix.Length..];
        if (!_catalogs.TryGetValue(id, out var catalog) || catalog is null)
        {
            throw new KeyNotFoundException("Reference catalog is unavailable.");
        }

        if (!parameters.TryGetValue("record", out var value) || value.Kind != FieldValueKind.Text ||
            catalog.Find(value.AsString()) is not { } record)
        {
            throw new ArgumentException("Choose an existing catalog record.", nameof(parameters));
        }

        var attributes = string.Join("\n", record.Attributes.Select(pair => $"{pair.Key}: {pair.Value}"));
        return new ActionOutcome { Message = $"{record.Name} · ID {record.Key} · {record.Category ?? "Unknown category"}\n" +
            $"{record.Description}\n{attributes}\n{record.AttributionText}\nInspection only; the save was not changed." };
    }

    private static ActionNode CatalogAction(IReferenceCatalog catalog)
    {
        var first = catalog.Search(new ReferenceQuery { Limit = 1 }).Items.FirstOrDefault();
        return new ActionNode(ActionPrefix + catalog.Id, "Inspect catalog record")
        {
            Description = "Browse/search identifiers. This action does not add, replace or unlock anything. " + catalog.Dataset.Attribution.ToShortText(),
            Parameters = [new FieldNode("record", "Reference record", new ReferenceSpec(catalog.Id), FieldValue.Of(first?.Key ?? ""))
                { Description = first?.AttributionText, Confidence = Confidence.Inferred }],
        };
    }

    public static uint FactHash(string canonicalName)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var value in Encoding.UTF8.GetBytes(canonicalName))
        {
            hash = unchecked((hash ^ value) * prime);
        }

        return hash;
    }
}
