using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed class GearboxTables(GearboxDocument document, GearboxLazyViews lazy)
{
    public IReadOnlyList<ViewNode> Inventory()
    {
        return document.Profile ? [] : [lazy.Register("slots", "Equipped slots", Slots)];
    }

    public IReadOnlyList<ViewNode> Progression()
    {
        List<ViewNode> nodes = [lazy.Register("sdus", "Storage deck upgrades", Sdus)];
        if (document.Profile) return nodes;
        nodes.Add(document.Integer("world/playthrough", "Last playthrough index", () => document.Root, "last_play_through_index", writable: false));
        nodes.Add(document.Integer("world/completed", "Playthroughs completed", () => document.Root, "playthroughs_completed", writable: false));
        nodes.Add(lazy.Register("ammo", "Ammo pools", Ammo));
        nodes.Add(lazy.Register("difficulty", "Mayhem / Chaos levels", () => Difficulty(0)));
        nodes.AddRange(document.Missions.Build());
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
                get, "mayhem_level", document.Root.Schema.Wonderlands ? 100 : 11, warning: SerialWrites.Warning) with
                { Confidence = Confidence.Inferred, Description = "Existing playthrough difficulty only. Unlock challenges, modifier seeds and rewards are unchanged. " + SerialWrites.Warning });
            if (document.Root.Schema.Wonderlands)
                nodes.Add(document.Integer($"world/{i}/unlocked", $"Chaos unlocked (playthrough {i})", get, "mayhem_unlocked_level", 100, warning: SerialWrites.Warning) with
                    { Confidence = Confidence.Inferred, Description = "Raw unlocked ceiling; trial milestones and rewards are unchanged. " + SerialWrites.Warning });
        }
        if (offset + GearboxLazyViews.PageSize < states)
            nodes.Add(lazy.Register($"difficulty/{offset + GearboxLazyViews.PageSize}", "More playthroughs", () => Difficulty(offset + GearboxLazyViews.PageSize)));
        return nodes;
    }

    private IReadOnlyList<ViewNode> Slots() => MessageTable("equipped_inventory_list", "slots", [new("slot", "Slot asset"), new("index", "Inventory index"), new("enabled", "Enabled")],
        (m, i) => Row(i.ToString(),
            ("slot", document.AssetLabel($"slots/{i}/label", "Slot", m.Text("slot_data_path"))), ("index", new TextNode(m.Integer("inventory_list_index").ToString())),
            ("enabled", new TextNode((m.Integer("enabled") != 0).ToString()))));

    private IReadOnlyList<ViewNode> Sdus()
    {
        var list = document.Profile ? "profile_sdu_list" : "sdu_list";
        return MessageTable(list, "sdus", [new("asset", "SDU asset"), new("level", "Purchased level")], (m, i) => Row(i.ToString(),
            ("asset", document.AssetLabel($"sdu/{i}/label", "SDU", m.Text("sdu_data_path"))),
            ("level", Sdu(list, m, i))));
    }

    private ViewNode Sdu(string list, ProtoMessage message, int index)
    {
        var maximum = GearboxWritePolicies.SduMaximum(message.Text("sdu_data_path"), document.Root.Schema.Wonderlands);
        if (maximum is null) return document.Integer($"sdu/{index}", "Purchased level", () => document.Root.Children(list).ElementAt(index), "sdu_level", writable: false);
        return document.ItemNumber($"sdu/{index}", "Purchased level", 0, maximum.Value,
            () => checked((int)document.Root.Children(list).ElementAt(index).Integer("sdu_level")), value =>
            {
                var current = document.Root.Children(list).ElementAt(index);
                current.SetInteger("sdu_level", value);
                var path = current.Text("sdu_data_path");
                var inventory = path.Contains("SDU_Backpack.", StringComparison.Ordinal) ? GearboxInventory.Backpack :
                    path.Contains("SDU_Bank.", StringComparison.Ordinal) ? GearboxInventory.Bank :
                    path.Contains("SDU_LostLoot.", StringComparison.Ordinal) ? GearboxInventory.LostLoot : null;
                if (inventory is not null) document.Inventory.EnsureCapacity(inventory, 0);
            });
    }

    private IReadOnlyList<ViewNode> Ammo() => MessageTable("resource_pools", "ammo", [new("asset", "Resource pool"), new("amount", "Ammo")],
        (m, i) => Row(i.ToString(), ("asset", document.AssetLabel($"ammo/{i}/label", "Resource", m.Text("resource_path"))),
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

    private static TableRow Row(string key, params (string Key, ViewNode Value)[] cells) => new(key, cells.ToDictionary(c => c.Key, c => c.Value));
}
