using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed class GearboxTables(GearboxDocument document, GearboxLazyViews lazy)
{
    private const string ItemWarning = "Item serials are encrypted independently. Items, flags and slots are read-only; names are not decoded.";

    public IReadOnlyList<ViewNode> Inventory()
    {
        List<ViewNode> nodes = [new TextNode(ItemWarning) { Tone = Tone.Info }];
        var list = document.Profile ? "bank_inventory_list" : "inventory_items";
        nodes.Add(lazy.Register("items", document.Profile ? "Bank inventory" : "Backpack", () => ItemPage(list, 0)));
        if (document.Profile)
            nodes.Add(lazy.Register("lost-loot", "Lost loot", () => ItemPage("lost_loot_inventory_list", 0)));
        else nodes.Add(lazy.Register("slots", "Equipped slots", Slots));
        return nodes;
    }

    public IReadOnlyList<ViewNode> Progression()
    {
        List<ViewNode> nodes = [lazy.Register("sdus", "Storage deck upgrades", Sdus)];
        if (document.Profile) return nodes;
        nodes.Add(document.Integer("world/playthrough", "Last playthrough index", () => document.Root, "last_play_through_index", writable: false));
        nodes.Add(document.Integer("world/completed", "Playthroughs completed", () => document.Root, "playthroughs_completed", writable: false));
        nodes.Add(lazy.Register("ammo", "Ammo pools", Ammo));
        nodes.Add(lazy.Register("difficulty", "Mayhem / Chaos levels", () => Difficulty(0)));
        nodes.Add(lazy.Register("missions", "Missions", () => Missions(0)));
        return nodes;
    }

    private IReadOnlyList<ViewNode> Difficulty(int offset)
    {
        var nodes = new List<ViewNode>();
        var states = document.Root.All("game_state_save_data_for_playthrough").Count();
        for (var i = offset; i < Math.Min(states, offset + GearboxLazyViews.PageSize); i++)
        {
            var index = i;
            Func<ProtoMessage> get = () => document.Root.Children("game_state_save_data_for_playthrough").ElementAt(index);
            nodes.Add(document.Integer($"world/{i}/difficulty", document.Root.Schema.Wonderlands ? $"Chaos level (playthrough {i})" : $"Mayhem level (playthrough {i})",
                get, "mayhem_level", writable: false));
            if (document.Root.Schema.Wonderlands)
                nodes.Add(document.Integer($"world/{i}/unlocked", $"Chaos unlocked (playthrough {i})", get, "mayhem_unlocked_level", writable: false));
        }
        if (offset + GearboxLazyViews.PageSize < states)
            nodes.Add(lazy.Register($"difficulty/{offset + GearboxLazyViews.PageSize}", "More playthroughs", () => Difficulty(offset + GearboxLazyViews.PageSize)));
        return nodes;
    }

    private IReadOnlyList<ViewNode> ItemPage(string list, int offset)
    {
        var all = document.Root.All(list).ToArray();
        var rows = all.Skip(offset).Take(GearboxLazyViews.PageSize).Select((f, i) =>
        {
            var serial = f.Message?.Last("item_serial_number")?.Value as byte[] ?? f.Value as byte[] ?? [];
            var index = offset + i;
            return Row(index.ToString(), ("index", new TextNode(index.ToString())), ("bytes", new TextNode(serial.Length.ToString())),
                ("flags", new TextNode(f.Message?.Integer("flags").ToString() ?? "—")),
                ("pickup", new TextNode(f.Message?.Integer("pickup_order_index").ToString() ?? "—")));
        }).ToArray();
        List<ViewNode> result = [new TableNode([new("index", "List index"), new("bytes", "Serial bytes"), new("flags", "Flags"), new("pickup", "Pickup order")], rows)];
        if (offset + GearboxLazyViews.PageSize < all.Length)
            result.Add(lazy.Register($"items/{list}/{offset + GearboxLazyViews.PageSize}", "More items", () => ItemPage(list, offset + GearboxLazyViews.PageSize)));
        return result;
    }

    private IReadOnlyList<ViewNode> Slots() => MessageTable("equipped_inventory_list", "slots", [new("slot", "Slot asset"), new("index", "Inventory index"), new("enabled", "Enabled")],
        (m, i) => Row(i.ToString(),
            ("slot", new TextNode(m.Text("slot_data_path"))), ("index", new TextNode(m.Integer("inventory_list_index").ToString())),
            ("enabled", new TextNode((m.Integer("enabled") != 0).ToString()))));

    private IReadOnlyList<ViewNode> Sdus()
    {
        var list = document.Profile ? "profile_sdu_list" : "sdu_list";
        return MessageTable(list, "sdus", [new("asset", "SDU asset"), new("level", "Purchased level")], (m, i) => Row(i.ToString(),
            ("asset", new TextNode(m.Text("sdu_data_path"))),
            ("level", document.Integer($"sdu/{i}", "Purchased level", () => document.Root.Children(list).ElementAt(i), "sdu_level", writable: false))));
    }

    private IReadOnlyList<ViewNode> Ammo() => MessageTable("resource_pools", "ammo", [new("asset", "Resource pool"), new("amount", "Ammo")],
        (m, i) => Row(i.ToString(), ("asset", new TextNode(m.Text("resource_path"))),
            ("amount", float.IsFinite(m.Float("amount")) ? document.Ammo($"ammo/{i}", () => document.Root.Children("resource_pools").ElementAt(i))
                : new TextNode("Non-finite resource amount (preserved, read-only)"))));

    private IReadOnlyList<ViewNode> MessageTable(string list, string id, IReadOnlyList<TableColumn> columns, Func<ProtoMessage, int, TableRow> row, int offset = 0)
    {
        var messages = document.Root.Children(list).ToArray();
        List<ViewNode> result = [new TableNode(columns, messages.Skip(offset).Take(GearboxLazyViews.PageSize).Select((m, i) => row(m, i + offset)).ToArray())];
        if (offset + GearboxLazyViews.PageSize < messages.Length)
            result.Add(lazy.Register($"{id}/{offset + GearboxLazyViews.PageSize}", "More records", () => MessageTable(list, id, columns, row, offset + GearboxLazyViews.PageSize)));
        return result;
    }

    private IReadOnlyList<ViewNode> Missions(int offset)
    {
        var all = document.Root.Children("mission_playthroughs_data").SelectMany((p, i) => p.Children("mission_list").Select(m => (Index: i, Mission: m))).ToArray();
        var rows = all.Skip(offset).Take(GearboxLazyViews.PageSize).Select((v, i) => Row((offset + i).ToString(),
            ("playthrough", new TextNode(v.Index.ToString())), ("mission", new TextNode(v.Mission.Text("mission_class_path"))),
            ("status", new TextNode(v.Mission.Integer("status") switch { 0 => "Not started", 1 => "Active", 2 => "Complete", 3 => "Failed", _ => "Unknown" })))).ToArray();
        List<ViewNode> result = [new TableNode([new("playthrough", "Playthrough"), new("mission", "Mission asset"), new("status", "Status")], rows)];
        if (offset + GearboxLazyViews.PageSize < all.Length)
            result.Add(lazy.Register($"missions/{offset + GearboxLazyViews.PageSize}", "More missions", () => Missions(offset + GearboxLazyViews.PageSize)));
        return result;
    }

    private static TableRow Row(string key, params (string Key, ViewNode Value)[] cells) => new(key, cells.ToDictionary(c => c.Key, c => c.Value));
}
