using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

internal sealed class PragmataViews
{
    internal const string QuantityAction = "inventory/set-quantities";
    private readonly PragmataDocument _document;
    private readonly int _blockCount;
    private readonly LazyPages _pages = new();
    private readonly RawExplorer _raw;
    private readonly InventoryView _inventory;

    public PragmataViews(PragmataDocument document, int blockCount)
    {
        _document = document;
        _blockCount = blockCount;
        _raw = new RawExplorer(document, _pages);
        _inventory = new InventoryView(document, _pages);
    }

    public ViewNode Build()
    {
        var tabs = new List<Tab> { new("Overview", Overview()) };
        if (_document.Layout.Inventory is not null)
        {
            tabs.Add(new Tab("Inventory", _inventory.Build()));
            tabs.Add(new Tab("Clock", Clock()));
        }
        tabs.Add(new Tab("Raw", [
            new TextNode("Fixed-size primitives are editable by exact path. Strings, structs, array sizes and metadata are read-only. Raw aliases and their primary controls stay synchronized.") { Tone = Tone.Info },
            .. _raw.Roots(),
        ]));
        return new TabsNode(tabs);
    }

    public IReadOnlyList<ViewNode> Expand(string id) => _pages.Expand(id);

    private IReadOnlyList<ViewNode> Overview()
    {
        var layout = _document.Layout;
        var stats = new List<Stat>
        {
            new("Slot", _document.Read(layout.Detail.Find(KnownHashes.Slot)!).ToString()),
            new("Integrity", $"Murmur3 + {_blockCount} CityHash64 blocks + embedded key/IV verified"),
        };
        var fields = new List<ViewNode>();
        if (layout.Header is { } header)
        {
            var ticks = _document.Read(header.Find(KnownHashes.SaveDate)!).AsInt64();
            var date = ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks
                ? new DateTime(ticks, DateTimeKind.Unspecified).ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture)
                : $"Invalid date ticks: {ticks}";
            stats.Add(new Stat("Save date", date) { Hint = "Console timezone is unknown; ticks interpreted as .NET ticks." });
            stats.Add(new Stat("Playtime (hours)", ((double)_document.Read(header.Find(KnownHashes.Playtime)!).AsUInt64() / 3_600_000_000).ToString("F3", CultureInfo.InvariantCulture)) { Hint = "Units inferred from all 11 gameplay samples." });
            stats.Add(new Stat("Cleared", _document.Read(header.Find(KnownHashes.Cleared)!).ToString()));
            stats.Add(new Stat("Difficulty hash", $"0x{_document.Read(header.Find(KnownHashes.Difficulty)!).AsUInt32():x8}") { Hint = "Difficulty ID meaning unknown." });
            fields.Add(new SectionNode("Save header", header.Children.Select(n => (ViewNode)_document.Field(n)).ToArray()));
        }
        fields.Add(new SectionNode("File detail", layout.Detail.Children.Select(n => (ViewNode)new TextNode($"{HashNames.Label(n.Hash)}: {_document.PreviewOrValue(n)}")).ToArray()));
        return [new StatsNode(stats), new TextNode(PragmataDocument.EditWarning) { Tone = Tone.Warning }, .. fields];
    }

    private IReadOnlyList<ViewNode> Clock() => [
        new TextNode("Units are inferred as microseconds. Editing playtime adjusts elapsed time, preserving demo and pause. Editing any clock recomputes playtime. Conflicting batches and unsigned overflow/underflow are rejected.") { Tone = Tone.Info },
        .. _document.Layout.Timers.Select(n => _document.Field(n)),
    ];
}
