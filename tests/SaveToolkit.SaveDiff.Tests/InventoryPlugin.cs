using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.SaveDiff.Tests;

/// <summary>
/// Read-only fake plugin for decoder tests. Format: <c>INV1</c> line, then <c>itemId=count</c> lines.
/// Value ids use the item id (record identity), like real plugins should; items sit in a lazy section.
/// </summary>
internal sealed class InventoryPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("test.inventory", "Inventory test format", "1.0.0") { Maturity = PluginMaturity.ReadOnly };

    public static byte[] Create(params (int Id, int Count)[] items) =>
        TestSaves.Utf8("INV1\n" + string.Concat(items.Select(i => $"{i.Id}={i.Count}\n")));

    public ProbeResult Probe(ISaveFile file) =>
        file.ReadHeader(4).AsSpan().SequenceEqual("INV1"u8) ? ProbeResult.Match(ProbeScore.Title) : ProbeResult.None;

    public ISaveDocument Open(ISaveFile file)
    {
        var lines = System.Text.Encoding.UTF8.GetString(file.ReadAllBytes()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0] != "INV1")
        {
            throw new SaveFormatException("Not an inventory save.");
        }

        var items = lines.Skip(1).Select(l => l.Split('=')).ToDictionary(p => p[0], p => long.Parse(p[1], CultureInfo.InvariantCulture), StringComparer.Ordinal);
        return new Document(items);
    }

    private sealed class Document(IReadOnlyDictionary<string, long> items) : ISaveDocument
    {
        public string Title => "Inventory";

        public bool CanEdit => false;

        public ViewNode BuildView() => new SectionNode("Inventory",
        [
            new FieldNode("summary/count", "Distinct items", IntegerSpec.Int32, FieldValue.Of(items.Count)),
            SectionNode.Lazy("Items", "items"),
            new ActionNode("noop", "Not a value") { Parameters = [new FieldNode("param", "Param", IntegerSpec.Int32, FieldValue.Of(0L))] },
        ]);

        public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => lazyId == "items"
            ? [.. items.Select(kv => new FieldNode($"inventory/{kv.Key}/count", $"Item {kv.Key}", IntegerSpec.Int32, FieldValue.Of(kv.Value)))]
            : throw new KeyNotFoundException(lazyId);

        public FieldValue GetValue(string valueId) => valueId == "summary/count"
            ? FieldValue.Of(items.Count)
            : FieldValue.Of(items[valueId.Split('/')[1]]);

        public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => EditOutcome.Rejected(edits[0].ValueId, "Read-only.");

        public IReadOnlyList<FileOutput> Serialize() => [];

        public void Dispose()
        {
        }
    }
}
