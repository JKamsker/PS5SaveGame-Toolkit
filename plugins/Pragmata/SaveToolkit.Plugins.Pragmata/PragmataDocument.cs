using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.Pragmata;

internal sealed class PragmataDocument : ISaveDocument, IQueryableDocument, IActionPlanningDocument
{
    internal const string EditWarning = "Experimental: not validated on console. Game limits and side effects are unverified. " +
        "CityHash64 and Murmur3 are repaired; the RSA trailer is preserved but cannot be validated or regenerated. The game may reject edited saves.";
    private const int MaximumStringPreviewBytes = 2048;
    private const int MaximumStructPreviewBytes = 64;
    private const long MicrosecondsPerSecond = 1_000_000;
    private readonly string _relativePath;
    private readonly DsssContainer _container;
    private readonly Dictionary<string, SaveNode> _bindings = new(StringComparer.Ordinal);
    private readonly PragmataViews _views;
    private readonly QuantityActions _quantityActions;
    private byte[] _plaintext;

    public SaveLayout Layout { get; }
    internal ReferenceReadouts References { get; }
    internal EvidenceCollections Evidence { get; }
    internal InventoryCollections InventoryRows { get; }
    internal MissionReferences Missions { get; }
    internal ObservedEditBounds Bounds { get; }
    public IReadOnlyList<QueryableCollection> Collections => InventoryRows.Enabled ? [InventoryRows.Metadata, .. Evidence.Metadata] : Evidence.Metadata;
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => collectionId == InventoryCollections.CollectionId
        ? InventoryRows.Query(query) : Evidence.Query(collectionId, query);
    public bool CanEdit => true;
    public string Title => Layout.Header is { } header
        ? FormattableString.Invariant($"Pragmata · Slot {Read(Layout.Detail.Find(KnownHashes.Slot)!)} · {HoursPlayed(header):F1} h")
        : "Pragmata · System data";

    public PragmataDocument(string relativePath, ReadOnlySpan<byte> original, IReferenceContext? references = null)
    {
        _relativePath = relativePath;
        _container = new DsssContainer(original);
        _plaintext = _container.Plaintext.ToArray();
        Layout = new SaveLayout(_plaintext);
        Bounds = new ObservedEditBounds(Layout, _plaintext);
        References = new ReferenceReadouts(this, references ?? IReferenceContext.Empty);
        Evidence = new EvidenceCollections(this);
        InventoryRows = new InventoryCollections(this);
        Missions = new MissionReferences(this);
        _quantityActions = new QuantityActions(this);
        _views = new PragmataViews(this, _container.BlockCount);
    }

    public ViewNode BuildView() => _views.Build();
    public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _views.Expand(lazyId);
    public FieldValue GetValue(string valueId) => References.GetValue(valueId) ??
        Read(_bindings.TryGetValue(valueId, out var node) ? node :
            throw new KeyNotFoundException($"Unknown Pragmata value '{valueId}'."));

    public AssetContent? GetAsset(string assetId) => References.GetAsset(assetId);

    internal FieldValue Read(SaveNode node) => node.Type == FieldTags.String
        ? FieldValue.Of(Encoding.Unicode.GetString(_plaintext, node.Offset, node.Size))
        : PrimitiveCodec.Read(node, _plaintext);

    internal string Preview(SaveNode node) => node.Type == FieldTags.String
        ? Encoding.Unicode.GetString(_plaintext, node.Offset, Math.Min(node.Size, MaximumStringPreviewBytes)) + (node.Size > MaximumStringPreviewBytes ? "…" : "")
        : Convert.ToHexString(_plaintext.AsSpan(node.Offset, Math.Min(node.Size, MaximumStructPreviewBytes))) + (node.Size > MaximumStructPreviewBytes ? "…" : "");

    internal string PreviewOrValue(SaveNode node) => node.IsPrimitive ? Read(node).ToString() : Preview(node);
    internal string Fingerprint(SaveNode node) => "opaque-sha256:" + Convert.ToHexStringLower(SHA256.HashData(_plaintext.AsSpan(node.Offset, node.Size)));
    internal bool HasPrimaryBinding(SaveNode node) => _bindings.ContainsKey(node.Path);

    internal double HoursPlayed(SaveNode header) => (double)Read(header.Find(KnownHashes.Playtime)!).AsUInt64() /
        (MicrosecondsPerSecond * TimeSpan.SecondsPerHour);

    internal FieldNode Field(SaveNode node, bool raw = false)
    {
        var id = raw ? $"raw/{node.Path}" : node.Path;
        _bindings[id] = node;
        var spec = Bounds.Spec(node);
        if (IsTimer(node) && spec is IntegerSpec timer)
            spec = timer with { Format = IntegerFormat.Duration, UnitsPerSecond = MicrosecondsPerSecond, Unit = "µs" };
        else if (spec is IntegerSpec integer && HashNames.All.TryGetValue(node.Hash, out var name) && name.EndsWith("Hash", StringComparison.Ordinal))
            spec = integer with { Format = IntegerFormat.Hex };
        return new FieldNode(id, HashNames.Label(node.Hash), spec, Read(node))
        {
            Confidence = HashNames.All.ContainsKey(node.Hash) ? Confidence.Inferred : Confidence.Unknown,
            Description = $"{node.Path} · {FieldTags.ShortName(node.Type)} @0x{node.Offset:x} · {References.IdentifierSource(node.Hash)} " +
                $"{Bounds.Description(node)} These are editor/storage bounds, not proven game limits. {EditWarning}",
        };
    }

    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var proposed = new Dictionary<SaveNode, FieldValue>();
        foreach (var edit in edits)
        {
            var resolved = References.Resolve(edit.ValueId, edit.Value);
            if (resolved?.Error is { } referenceError) return EditOutcome.Rejected(edit.ValueId, referenceError);
            var value = resolved?.Value ?? edit.Value;
            var node = resolved?.Node ?? _bindings.GetValueOrDefault(edit.ValueId);
            if (node is null)
                return EditOutcome.Rejected(edit.ValueId, "Unknown field; expand its section first.");
            if (!node.IsPrimitive) return EditOutcome.Rejected(edit.ValueId, "Field is read-only.");
            if (Bounds.Validate(node, value) is { } error)
                return EditOutcome.Rejected(edit.ValueId, error);
            value = node.Type == FieldTags.Float32 ? FieldValue.Of((double)(float)value.AsDouble()) : value;
            if (proposed.TryGetValue(node, out var previous) && previous != value)
                return EditOutcome.Rejected(edit.ValueId, "Conflicting edits address the same primitive.");
            proposed[node] = value;
        }
        if (ClockEdits.Reconcile(Layout, _plaintext, proposed) is { } clockError)
            return EditOutcome.Rejected(edits[0].ValueId, clockError);

        // Clock values must be addressable for undo even if the caller has only expanded Raw.
        foreach (var timer in proposed.Keys.Where(IsTimer)) _ = Field(timer);
        var changes = ChangesFor(proposed);
        if (changes.Count == 0) return new EditOutcome();

        // Stage bytes so a failed batch cannot partially mutate the document.
        var staged = _plaintext.ToArray();
        foreach (var (node, value) in proposed)
            if (Read(node) != value) PrimitiveCodec.Write(node, staged, value);
        _plaintext = staged;
        return new EditOutcome { Changes = changes, Warnings = [EditWarning], StructureChanged = true };
    }

    internal IReadOnlyList<ValueChange> ChangesFor(IReadOnlyDictionary<SaveNode, FieldValue> proposed) =>
        _bindings.Where(b => proposed.TryGetValue(b.Value, out var next) && Read(b.Value) != next)
            .Select(b => new ValueChange(b.Key, Read(b.Value), proposed[b.Value]))
            .Concat(References.Changes(proposed)).ToArray();

    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        actionId == MissionReferences.InspectAction ? Missions.Inspect(parameters) :
            throw new InvalidOperationException("Pragmata bulk actions require a preview before applying.");

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        _quantityActions.Plan(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
        _quantityActions.Apply(actionId, parameters, scope, plan);

    public IReadOnlyList<FileOutput> Serialize()
    {
        var output = _container.Rebuild(_plaintext);
        var verified = new DsssContainer(output);
        if (!verified.Plaintext.AsSpan().SequenceEqual(_plaintext))
            throw new SaveFormatException("Pragmata serialization verification produced different plaintext.");
        _ = new SaveLayout(verified.Plaintext);
        return [new FileOutput(_relativePath, output)];
    }

    private bool IsTimer(SaveNode node) => node == Layout.Header?.Find(KnownHashes.Playtime) || Layout.Timers.Contains(node);
    public void Dispose() { }
}
