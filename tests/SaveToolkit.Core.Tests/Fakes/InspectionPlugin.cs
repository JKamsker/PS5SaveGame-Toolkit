using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Independent synthetic documents, including deliberately broken inspection implementations.</summary>
public sealed class InspectionPlugin(bool editable = false) : IGamePlugin
{
    public PluginInfo Info { get; } = new("test.items", "Inspection fixture", "1.0.0")
    {
        Games = [new GameTitle(ItemsPlugin.TitleId, "Inspection fixture")],
    };

    public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Title, "Inspection fixture");
    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);
    public ISaveDocument Open(ISaveFile file, IReferenceContext references) => new Document(file, references, editable);

    private sealed class Document(ISaveFile file, IReferenceContext references, bool editable) : BoundSaveDocument
    {
        private readonly byte[] _bytes = file.ReadAllBytes();
        private long _transient;
        public override string Title => "Inspection fixture";
        public override bool CanEdit => editable;

        public override ViewNode BuildView() => new SectionNode("Inspection", [
            Field("number", "Number", IntegerSpec.UInt8, () => FieldValue.Of((long)_bytes[0]), v => _bytes[0] = (byte)v.AsInt32()),
            Field("transient", "Transient", IntegerSpec.Int64, () => FieldValue.Of(_transient)),
            Field("item", "Item", new ReferenceSpec("items"), () => FieldValue.Of("1001")),
            Field("timer", "Timer", FloatSpec.Single with { Format = FloatFormat.Duration, UnitsPerSecond = 1000 }, () => FieldValue.Of(61250.5)),
            new ActionNode("legacy", "Legacy"),
            Inspect("inspect"), Inspect("reported"), Inspect("silent"), Inspect("hidden"), Inspect("transient-change"),
            Inspect("structural"), Inspect("irreversible"), Inspect("memento"), Inspect("throws"),
            Inspect("planned") with { SupportsPlanning = true },
            Inspect("parameter") with { Parameters = [new FieldNode("value", "Value", new IntegerSpec(0, 9), FieldValue.Of(7L))] },
            SectionNode.Lazy("Lazy details", "details"),
        ]);

        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => lazyId == "details"
            ? [Field("lazy", "Lazy", IntegerSpec.UInt8, () => FieldValue.Of((long)_bytes[0])), Inspect("lazy-inspect")]
            : base.ExpandNode(lazyId);

        private static ActionNode Inspect(string id) => new(id, id) { Mutates = false };

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        {
            switch (actionId)
            {
                case "inspect":
                    var label = references.Catalog("items")?.Find("1001")?.Name ?? "unresolved";
                    return new ActionOutcome { Message = $"{label}: {_bytes[0]}", Warnings = ["Inspection only."] };
                case "lazy-inspect":
                    return new ActionOutcome { Message = GetValue("lazy").ToString() };
                case "parameter":
                    return new ActionOutcome { Message = parameters["value"].ToString() };
                case "reported":
                    return new ActionOutcome { Changes = [new ValueChange("number", FieldValue.Of(5L), FieldValue.Of(9L))] };
                case "silent":
                    _bytes[0]++;
                    return new ActionOutcome();
                case "transient-change":
                    _transient++;
                    return new ActionOutcome();
                case "hidden":
                    _bytes[1]++;
                    return new ActionOutcome();
                case "structural": return new ActionOutcome { StructureChanged = true };
                case "irreversible": return new ActionOutcome { Undoable = false };
                case "memento": return new ActionOutcome { Revert = new Token() };
                case "throws":
                    _bytes[0]++;
                    throw new InvalidOperationException("Failed after mutation.");
                default: return base.InvokeAction(actionId, parameters);
            }
        }

        public override IReadOnlyList<FileOutput> Serialize() => [new(file.RelativePath, _bytes)];
    }

    private sealed class Token : DocumentMemento
    {
        public override long SizeBytes => 1;
    }
}
