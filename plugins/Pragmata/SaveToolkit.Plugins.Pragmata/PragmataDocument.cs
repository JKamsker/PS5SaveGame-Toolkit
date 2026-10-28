using System.Text;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata;

internal sealed class PragmataDocument : ISaveDocument
{
    internal const string EditWarning = "Experimental: valid game ranges and console loading have not been tested.";
    private const int MaximumStringPreviewBytes = 2048;
    private const int MaximumStructPreviewBytes = 64;
    private const long MicrosecondsPerSecond = 1_000_000;
    private readonly string _relativePath;
    private readonly DsssContainer _container;
    private readonly Dictionary<string, SaveNode> _bindings = new(StringComparer.Ordinal);
    private readonly HashSet<SaveNode> _exposedNodes = [];
    private readonly PragmataViews _views;
    private byte[] _plaintext;

    public SaveLayout Layout { get; }
    public bool CanEdit => true;
    public string Title => Layout.Header is { } header
        ? FormattableString.Invariant($"Pragmata · Slot {Read(Layout.Detail.Find(KnownHashes.Slot)!)} · {HoursPlayed(header):F1} h")
        : "Pragmata · System data";

    public PragmataDocument(string relativePath, ReadOnlySpan<byte> original)
    {
        _relativePath = relativePath;
        _container = new DsssContainer(original);
        _plaintext = _container.Plaintext.ToArray();
        Layout = new SaveLayout(_plaintext);
        _views = new PragmataViews(this, _container.BlockCount);
    }

    public ViewNode BuildView() => _views.Build();
    public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _views.Expand(lazyId);
    public FieldValue GetValue(string valueId) => Read(_bindings.TryGetValue(valueId, out var node) ? node :
        throw new KeyNotFoundException($"Unknown Pragmata value '{valueId}'."));

    internal FieldValue Read(SaveNode node) => node.Type == FieldTags.String
        ? FieldValue.Of(Encoding.Unicode.GetString(_plaintext, node.Offset, node.Size))
        : PrimitiveCodec.Read(node, _plaintext);

    internal string Preview(SaveNode node) => node.Type == FieldTags.String
        ? Encoding.Unicode.GetString(_plaintext, node.Offset, Math.Min(node.Size, MaximumStringPreviewBytes)) + (node.Size > MaximumStringPreviewBytes ? "…" : "")
        : Convert.ToHexString(_plaintext.AsSpan(node.Offset, Math.Min(node.Size, MaximumStructPreviewBytes))) + (node.Size > MaximumStructPreviewBytes ? "…" : "");

    internal string PreviewOrValue(SaveNode node) => node.IsPrimitive ? Read(node).ToString() : Preview(node);

    internal double HoursPlayed(SaveNode header) => (double)Read(header.Find(KnownHashes.Playtime)!).AsUInt64() /
        (MicrosecondsPerSecond * TimeSpan.SecondsPerHour);

    internal FieldNode Field(SaveNode node, bool raw = false)
    {
        var id = raw ? $"raw/{node.Path}" : node.Path;
        _bindings[id] = node;
        _exposedNodes.Add(node);
        var spec = PrimitiveCodec.Spec(node);
        if (IsTimer(node) && spec is IntegerSpec timer)
            spec = timer with { Format = IntegerFormat.Duration, UnitsPerSecond = MicrosecondsPerSecond, Unit = "µs" };
        else if (spec is IntegerSpec integer && HashNames.All.TryGetValue(node.Hash, out var name) && name.EndsWith("Hash", StringComparison.Ordinal))
            spec = integer with { Format = IntegerFormat.Hex };
        return new FieldNode(id, HashNames.Label(node.Hash), spec, Read(node))
        {
            Confidence = IsTimer(node) ? Confidence.Inferred : HashNames.All.ContainsKey(node.Hash) ? Confidence.Verified : Confidence.Unknown,
            Description = $"{node.Path} · {FieldTags.ShortName(node.Type)} @0x{node.Offset:x}",
        };
    }

    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var proposed = new Dictionary<SaveNode, FieldValue>();
        foreach (var edit in edits)
        {
            if (!_bindings.TryGetValue(edit.ValueId, out var node))
                return EditOutcome.Rejected(edit.ValueId, "Unknown field; expand its section first.");
            if (!node.IsPrimitive) return EditOutcome.Rejected(edit.ValueId, "Field is read-only.");
            if (PrimitiveCodec.Validate(node, edit.Value) is { } error)
                return EditOutcome.Rejected(edit.ValueId, error);
            var value = node.Type == FieldTags.Float32 ? FieldValue.Of((double)(float)edit.Value.AsDouble()) : edit.Value;
            if (proposed.TryGetValue(node, out var previous) && previous != value)
                return EditOutcome.Rejected(edit.ValueId, "Conflicting edits address the same primitive.");
            proposed[node] = value;
        }
        if (ClockEdits.Reconcile(Layout, _plaintext, proposed) is { } clockError)
            return EditOutcome.Rejected(edits[0].ValueId, clockError);

        // Clock values must be addressable for undo even if the caller has only expanded Raw.
        foreach (var timer in proposed.Keys.Where(IsTimer)) _ = Field(timer);
        var changes = _bindings.Where(b => proposed.TryGetValue(b.Value, out var next) && Read(b.Value) != next)
            .Select(b => new ValueChange(b.Key, Read(b.Value), proposed[b.Value])).ToArray();
        if (changes.Length == 0) return new EditOutcome();

        // Stage bytes so a failed batch cannot partially mutate the document.
        var staged = _plaintext.ToArray();
        foreach (var (node, value) in proposed)
            if (Read(node) != value) PrimitiveCodec.Write(node, staged, value);
        _plaintext = staged;
        return new EditOutcome { Changes = changes, Warnings = [EditWarning], StructureChanged = true };
    }

    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (actionId != PragmataViews.QuantityAction || Layout.Inventory is null)
            throw new KeyNotFoundException($"Unknown Pragmata action '{actionId}'.");
        if (parameters.Count != 1 || !parameters.TryGetValue("quantity", out var value) ||
            value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > int.MaxValue)
            throw new ArgumentException("Quantity must be an integer between 0 and 2147483647.", nameof(parameters));

        var undoable = Layout.Items.All(item => _exposedNodes.Contains(item.Find(KnownHashes.Quantity)!));
        var edits = Layout.Items.Select(item =>
        {
            var node = item.Find(KnownHashes.Quantity)!;
            _bindings[node.Path] = node;
            return new ValueEdit(node.Path, value);
        }).ToArray();
        var outcome = ApplyEdits(edits);
        if (!outcome.Succeeded) throw new InvalidOperationException(outcome.Errors[0].Message);
        undoable |= outcome.Changes.Count == 0;
        return new ActionOutcome
        {
            Changes = outcome.Changes, Warnings = undoable ? [EditWarning] : [EditWarning, "Some quantities were not exposed in a view; this bulk action clears undo history."],
            StructureChanged = outcome.StructureChanged, Undoable = undoable,
            Message = $"Set {Layout.Items.Count} existing item quantities to {value}.",
        };
    }

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
