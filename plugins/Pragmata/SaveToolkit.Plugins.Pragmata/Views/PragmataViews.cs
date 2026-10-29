using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

internal sealed class PragmataViews
{
    internal const string QuantityAction = "inventory/set-quantities";
    internal const string RestoreQuantityAction = "inventory/restore-imported-quantities";
    private readonly PragmataDocument _document;
    private readonly int _blockCount;
    private readonly LazyPages _pages = new();
    private readonly RawExplorer _raw;
    private readonly InventoryView _inventory;
    private readonly EvidenceView _evidence;

    public PragmataViews(PragmataDocument document, int blockCount)
    {
        _document = document;
        _blockCount = blockCount;
        _raw = new RawExplorer(document, _pages);
        _inventory = new InventoryView(document);
        _evidence = new EvidenceView(document, _pages);
    }

    public ViewNode Build()
    {
        var tabs = new List<Tab> { new("Overview", Overview()) };
        if (_document.Layout.Inventory is not null)
        {
            tabs.Add(new Tab("Inventory", [.. _inventory.Build(), .. new IllustratedReferences(_document, _pages).Build()]));
            tabs.Add(new Tab("Gadgets", _evidence.Gadgets()));
            tabs.Add(new Tab("Loadout", _evidence.Family("app.WeaponManager.SaveData", "app.EnhanceManager.SaveData",
                "app.EquipmentPresetManager.SaveData", "app.CharacterManager.GameSaveData")));
            tabs.Add(new Tab("Progress", _evidence.Family("app.ChapterManager.SaveData",
                "app.GameOccurrenceRecordManager.SaveData", "app.GameOccurrenceEventManager.SaveData", "app.CampaignManager.GameData",
                "app.DifficultyLevelSystem.SaveData")));
            tabs.Add(new Tab("Quests", [.. _document.Missions.Build(), .. _evidence.Family("app.ObjectiveManager.SaveData")]));
            tabs.Add(new Tab("World", [.. new CheckpointMap(_document, _pages).Build(), .. _evidence.Family("app.CheckPointManager.SaveData", "app.GimmickManager.SaveData",
                "app.PropManager.SaveData", "app.EnvironmentSceneManager.SaveData", "app.LightManager.SaveData")]));
            tabs.Add(new Tab("Clock", Clock()));
        }
        tabs.Add(new Tab("Raw", [
            new TextNode("Fixed-size primitives are editable by exact path. Strings, structs, array sizes and metadata are read-only. Raw aliases and their primary controls stay synchronized.") { Tone = Tone.Info },
            .. _raw.Roots(),
        ]));
        return new TabsNode(tabs.Select(tab => tab with
        {
            Children = [new TextNode(PragmataDocument.EditWarning) { Tone = Tone.Warning }, .. tab.Children],
        }).ToArray());
    }

    public IReadOnlyList<ViewNode> Expand(string id) => _pages.Expand(id);

    private IReadOnlyList<ViewNode> Overview()
    {
        var layout = _document.Layout;
        var stats = new List<Stat>
        {
            new("Slot", _document.Read(layout.Detail.Find(KnownHashes.Slot)!).ToString()),
            new("Integrity", "Verified") { Hint = $"Murmur3 + {_blockCount} CityHash64 blocks + embedded key/IV verified" },
        };
        var fields = new List<ViewNode>();
        if (layout.Header is { } header)
        {
            var ticks = _document.Read(header.Find(KnownHashes.SaveDate)!).AsInt64();
            var date = ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks
                ? new DateTime(ticks, DateTimeKind.Unspecified).ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture)
                : $"Invalid date ticks: {ticks}";
            stats.Add(new Stat("Save date", date) { Hint = "Console timezone is unknown; ticks interpreted as .NET ticks." });
            stats.Add(new Stat("Playtime (hours)", _document.HoursPlayed(header).ToString("F3", CultureInfo.InvariantCulture)) { Hint = "Units inferred from all 11 gameplay samples." });
            stats.Add(new Stat("Cleared", _document.Read(header.Find(KnownHashes.Cleared)!).ToString()));
            stats.Add(new Stat("Difficulty hash", $"0x{_document.Read(header.Find(KnownHashes.Difficulty)!).AsUInt32():x8}") { Hint = "Difficulty ID meaning unknown." });
            fields.Add(new SectionNode("Save header", header.Children.Select(n => _raw.Node(n, raw: false)).ToArray()));
        }
        fields.Add(new SectionNode("File detail", layout.Detail.Children.Select(n => (ViewNode)new TextNode($"{HashNames.Label(n.Hash)}: {_document.PreviewOrValue(n)} · {HashNames.Provenance(n.Hash)}")).ToArray()));
        return [new StatsNode(stats), .. fields];
    }

    private IReadOnlyList<ViewNode> Clock() => [
        new TextNode("Units are inferred as microseconds. Editing playtime adjusts elapsed time, preserving demo and pause. Editing any clock recomputes playtime. Conflicting batches and unsigned overflow/underflow are rejected.") { Tone = Tone.Info },
        .. _document.Layout.Timers.Select(n => _document.Field(n)),
    ];
}
