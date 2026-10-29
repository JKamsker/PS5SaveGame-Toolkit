using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Existing-entry metadata only. The SDK owns all PSF parsing and byte patching.</summary>
internal sealed class SuckerPunchCaption
{
    public const string ActionId = "metadata/set-save-caption";
    public const string ValueId = "metadata/sce_sys/param.sfo/SUBTITLE";
    public const string DetailsId = "metadata/save-caption-value";
    public const string Parameter = "caption";
    public const string SfoPath = "sce_sys/param.sfo";
    public const string Key = "SUBTITLE";
    public const string Label = "Save caption · sce_sys/param.sfo SUBTITLE";
    public const string Warning = TsushimaWriteWarnings.Console + " Experimental/Inferred manual metadata-only override. " +
        "Timer and difficulty are unchanged; native console acceptance is unverified. " +
        "Only this directory's existing sce_sys/param.sfo SUBTITLE changes; unknown bytes are retained. " +
        "Source: SaveToolkit SDK-10 existing-entry SFO contract; no automatic synchronization is established.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ISaveFile? _source;
    private ParamSfoDocument? _document;
    private string? _text;

    private SuckerPunchCaption(string unavailable) => Unavailable = "Save caption unavailable: " + unavailable;

    private SuckerPunchCaption(ISaveFile source, ParamSfoDocument document, string text, int capacity)
    {
        _source = source;
        _document = document;
        _text = text;
        Spec = new TextSpec { MaxUtf8Bytes = capacity };
    }

    public string? Unavailable { get; }
    public bool Available => _document is not null;
    public string? Text => _text;
    public TextSpec Spec { get; } = new();
    public IReadOnlyList<string> AdditionalOutputPaths => Available ? [_source!.PathInDirectory] : [];
    public byte[] Capture() => _document!.OriginalBytes.ToArray();

    public static SuckerPunchCaption Open(ISaveFile file, SaveContainer container, bool editable)
    {
        if (!file.Directory.TitleId.Equals(TsushimaIds.TitleId, StringComparison.OrdinalIgnoreCase) ||
            !(file.PathInDirectory.Equals("dis.dat", StringComparison.OrdinalIgnoreCase) ||
              file.PathInDirectory.Equals("memory.dat", StringComparison.OrdinalIgnoreCase)))
            return new("only editable Tsushima dis.dat or memory.dat is supported; Yōtei is deferred.");
        if (!editable || container.Kind != ContainerKind.Tsushima || container.Word3 is < TsushimaIds.OldestObservedVersion or > TsushimaIds.LatestObservedVersion)
            return new("an editable recognized Tsushima v47–v49 inventory is required.");
        var candidates = file.Directory.Files.Concat(file.Directory.SystemFiles)
            .Where(candidate => candidate.PathInDirectory.Equals(SfoPath, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (candidates.Length != 1)
            return new(candidates.Length == 0 ? "this directory has no sce_sys/param.sfo." : "duplicate sce_sys/param.sfo paths; metadata is preserved.");
        var source = file.Directory.FindAnyFile(SfoPath)!;
        if (source.Length > ParamSfoPatchLimits.Default.MaxFileBytes)
            return new("param.sfo exceeds the bounded SDK file limit; metadata is preserved.");
        ParamSfoDocument document;
        try { document = ParamSfoDocument.Parse(source.ReadAllBytes()); }
        catch (ParamSfoPatchException)
        {
            // Never include a malformed key or source bytes in user-facing diagnostics.
            return new("param.sfo is malformed; metadata is preserved.");
        }
        var entry = document.Entries.SingleOrDefault(entry => entry.Key == Key);
        if (entry is null) return new("SUBTITLE is absent; this action never adds a new key.");
        const ushort utf8 = 0x0204;
        const ushort utf8Special = 0x0004;
        if (entry.Format is not (utf8 or utf8Special))
            return new("SUBTITLE is not an existing supported UTF-8 entry; metadata is preserved.");
        ParamSfoPatchPlan initial;
        try { initial = document.PlanExistingUtf8(Key, ""); }
        catch (ParamSfoPatchException)
        {
            return new("SUBTITLE is not strict UTF-8 with a single final NUL, or has no terminator capacity; metadata is preserved.");
        }
        var capacity = Math.Min(entry.MaximumLength, ParamSfoPatchLimits.Default.MaxEncodedValueBytes) - 1;
        if (StrictUtf8.GetByteCount(initial.OriginalText) > capacity)
            return new("the existing SUBTITLE exceeds the writer limit.");
        return new(source, document, initial.OriginalText, capacity);
    }

    public IReadOnlyList<ViewNode> View()
    {
        if (!Available) return [new TextNode(Unavailable!) { Tone = Tone.Warning }];
        return [new TextNode($"param.sfo SUBTITLE: {_text}") { Tone = Tone.Info },
            SectionNode.Lazy("Caption value and provenance", DetailsId, "Existing SUBTITLE; manual metadata only"), new ActionNode(ActionId, "Set save caption (manual, Experimental)")
        {
            SupportsPlanning = true,
            Description = Warning,
            Parameters = [new FieldNode(Parameter, "Caption", Spec, Read())
            { Confidence = Confidence.Inferred, Description = Warning, Warning = Warning }],
        }];
    }

    public FieldNode FieldView => new(ValueId, Label, Spec, Read())
    { Confidence = Confidence.Inferred, Description = Warning, Warning = Warning };

    public FieldMetadata Metadata => new(ValueId, Label, Spec);
    public FieldValue Read() => FieldValue.Of(_text!);

    public string? Validate(FieldValue value)
    {
        if (!Available) return Unavailable;
        if (value.Kind != FieldValueKind.Text) return "Supply a text caption.";
        var text = value.AsString();
        if (text.Contains('\0', StringComparison.Ordinal)) return "NUL characters are not allowed.";
        if (text.Contains('\r', StringComparison.Ordinal) || text.Contains('\n', StringComparison.Ordinal))
            return "Caption must be a single line.";
        int used;
        try { used = StrictUtf8.GetByteCount(text); }
        catch (EncoderFallbackException) { return "Caption contains an invalid Unicode surrogate."; }
        if (used > Spec.MaxUtf8Bytes)
            return $"Caption uses {used} UTF-8 bytes; capacity {Spec.MaxUtf8Bytes}; remaining {Spec.MaxUtf8Bytes - used}.";
        try { _ = _document!.PlanExistingUtf8(Key, text); }
        catch (ParamSfoPatchException) { return "The existing SUBTITLE can no longer be patched; preview again."; }
        return null;
    }

    public ActionPlan Plan(IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (!Available) return Reject(Unavailable!);
        if (scope.RowKeys is not null || scope.QueryScope is not null)
            return Reject("Save caption accepts document scope only; no inventory or query selection.");
        if (parameters.Count != 1 || !parameters.TryGetValue(Parameter, out var value))
            return Reject("Supply exactly one text caption.");
        if (Validate(value) is { } error) return Reject(error);
        var patch = _document!.PlanExistingUtf8(Key, value.AsString());
        return new ActionPlan
        {
            Changes = patch.IsNoOp ? [] : [new(ValueId, Label, Read(), value)],
            Warnings = [Warning,
                $"Preview source SHA-256: {patch.OriginalFileSha256}; SUBTITLE entry SHA-256: {patch.OriginalEntrySha256}.",
                $"sce_sys/param.sfo metadata only: {StrictUtf8.GetByteCount(value.AsString())} / {Spec.MaxUtf8Bytes} UTF-8 payload bytes."],
        };
    }

    public void Write(FieldValue value)
    {
        if (Validate(value) is { } error) throw new SaveFormatException(error);
        Restore(_document!.Apply(_document.PlanExistingUtf8(Key, value.AsString())).Span);
    }

    public void Restore(ReadOnlySpan<byte> bytes)
    {
        var document = ParamSfoDocument.Parse(bytes);
        var current = document.PlanExistingUtf8(Key, "");
        _document = document;
        _text = current.OriginalText;
    }

    public FileOutput Serialize()
    {
        var bytes = Capture();
        _ = ParamSfoDocument.Parse(bytes).PlanExistingUtf8(Key, _text!);
        return new(_source!.RelativePath, bytes);
    }

    private static ActionPlan Reject(string message) => new() { Errors = [new(Parameter, message)], Warnings = [Warning] };
}
