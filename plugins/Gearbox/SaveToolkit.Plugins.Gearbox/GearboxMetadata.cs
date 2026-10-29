using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Targeted metadata registration for fields whose live bindings are already understood by Gearbox.</summary>
internal static class GearboxMetadata
{
    private const uint MoneyHash = 618814354;
    private const uint EridiumHash = 3679636065;
    private const uint GoldenKeyHash = 2420693941;

    internal static IReadOnlyList<FieldMetadata> Resolve(GearboxDocument document, IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count > FieldMetadataLimits.MaxBatchSize)
            throw new ArgumentOutOfRangeException(nameof(ids), ids.Count, $"Request at most {FieldMetadataLimits.MaxBatchSize} field IDs per batch.");
        foreach (var id in ids) ArgumentException.ThrowIfNullOrEmpty(id, nameof(ids));

        // Existing registrations are useful after a view build, while Ensure never walks a view.
        _ = document.ResolveRegistered(ids);
        foreach (var id in ids.Distinct(StringComparer.Ordinal)) Ensure(document, id);
        return document.ResolveRegistered(ids);
    }

    private static void Ensure(GearboxDocument document, string id)
    {
        if (TryActionObservation(document, id)) return;
        if (id is "character/name")
        {
            if (!document.Profile && Has(document.Root, "preferred_character_name"))
                _ = document.Text(id, "Character name", () => document.Root, "preferred_character_name", true,
                    NameLimit(document));
            return;
        }
        if (id is "character/level" or "character/xp")
        {
            if (!document.Profile)
            {
                document.EnsureProgressionBindings();
                if (id == "character/level") _ = document.Level();
                else _ = document.Experience();
            }
            return;
        }
        if (id is "character/skill-points" or "character/tree-grade")
        {
            if (!document.Profile && document.Root.Child("ability_data") is not null)
            {
                var name = id.EndsWith("skill-points", StringComparison.Ordinal) ? "ability_points" : "tree_grade";
                _ = document.Integer(id, name == "ability_points" ? "Unspent skill points" : "Skill tree grade",
                    () => document.Root.Child("ability_data")!, name,
                    warning: name == "ability_points" ? "Spent points are preserved; levelling down does not refund them." : "Advanced progression value; keep the existing value unless repairing a level-one character.");
            }
            return;
        }
        if (id is "character/recorded-level")
        {
            if (!document.Profile)
            {
                var index = document.Root.Children("game_stats_data").ToList().FindIndex(s =>
                    s.Text("stat_path") == CharacterProgression.LevelStat(document.Root.Schema.Wonderlands));
                if (index >= 0) _ = document.Integer(id, "Recorded level statistic",
                    () => document.Root.Children("game_stats_data").ElementAt(index), "stat_value",
                    warning: "Updated with XP; this statistic alone does not change level.");
            }
            return;
        }
        if (TryCurrency(document, id) || TryAmmo(document, id) || TrySdu(document, id) || TrySlot(document, id) || TryWorld(document, id) || TryAccount(document, id))
            return;
        TryItem(document, id);
    }

    private static bool TryCurrency(GearboxDocument document, string id)
    {
        var split = id.Split('/');
        if (split.Length != 2 || split[0] is not ("currency" or "keys") || !uint.TryParse(split[1], NumberStyles.None, CultureInfo.InvariantCulture, out var hash)) return false;
        if (document.Profile != (split[0] == "keys")) return false;
        var known = document.Profile ? hash == GoldenKeyHash : hash is MoneyHash or EridiumHash;
        if (!known) return false;
        var list = document.Profile ? "bank_inventory_category_list" : "inventory_category_list";
        if (!document.Root.All(list).Any(field => field.Message?.Integer("base_category_definition_hash") == hash)) return false;
        var label = document.Profile ? (document.Root.Schema.Wonderlands ? "Skeleton keys" : "Golden keys")
            : hash == MoneyHash ? "Money" : document.Root.Schema.Wonderlands ? "Moon orbs" : "Eridium";
        var maximum = hash == EridiumHash && document.Root.Schema.Wonderlands ? 16_000 : 2_000_000_000;
        _ = document.Category(list, hash, label, maximum, id);
        return true;
    }

    private static bool TryAmmo(GearboxDocument document, string id)
    {
        var split = id.Split('/');
        if (document.Profile || split.Length != 2 || split[0] != "ammo" || !int.TryParse(split[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return false;
        var pools = document.Root.Children("resource_pools").ToArray();
        if ((uint)index >= (uint)pools.Length) return false;
        _ = document.Ammo(id, () => document.Root.Children("resource_pools").ElementAt(index));
        return true;
    }

    private static bool TrySdu(GearboxDocument document, string id)
    {
        var split = id.Split('/');
        if (split.Length != 2 || split[0] != "sdu" || !int.TryParse(split[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return false;
        var list = document.Profile ? "profile_sdu_list" : "sdu_list";
        var rows = document.Root.Children(list).ToArray();
        if ((uint)index >= (uint)rows.Length) return false;
        var row = rows[index];
        var maximum = GearboxWritePolicies.SduMaximum(row.Text("sdu_data_path"), document.Root.Schema.Wonderlands);
        if (maximum is null || row.Integer("sdu_level") < 0 || row.Integer("sdu_level") > maximum.Value)
            _ = document.Integer(id, "Purchased level", () => document.Root.Children(list).ElementAt(index), "sdu_level", writable: false);
        else
            _ = document.ItemNumber(id, "Purchased level", 0, maximum.Value,
                () => checked((int)document.Root.Children(list).ElementAt(index).Integer("sdu_level")), value =>
                {
                    var current = document.Root.Children(list).ElementAt(index);
                    current.SetInteger("sdu_level", value);
                    if (GearboxWritePolicies.StorageList(current.Text("sdu_data_path")) is { } storageList)
                        document.Inventory.EnsureCapacity(storageList, 0);
                });
        return true;
    }

    private static bool TryWorld(GearboxDocument document, string id)
    {
        if (document.Profile) return false;
        if (id is "world/playthrough" or "world/completed")
        {
            var name = id.EndsWith("playthrough", StringComparison.Ordinal) ? "last_play_through_index" : "playthroughs_completed";
            if (Has(document.Root, name)) _ = document.Integer(id, id == "world/playthrough" ? "Last playthrough index" : "Playthroughs completed", () => document.Root, name, writable: false);
            return true;
        }
        var split = id.Split('/');
        if (split.Length != 3 || split[0] != "world" || !int.TryParse(split[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || split[2] is not ("difficulty" or "unlocked")) return false;
        var rows = document.Root.Children("game_state_save_data_for_playthrough").ToArray();
        if ((uint)index >= (uint)rows.Length) return false;
        var field = split[2] == "difficulty" ? "mayhem_level" : "mayhem_unlocked_level";
        if (split[2] == "unlocked" && !document.Root.Schema.Wonderlands || !Has(rows[index], field)) return false;
        _ = document.WorldDifficulty(index, split[2] == "unlocked");
        return true;
    }

    private static bool TrySlot(GearboxDocument document, string id)
    {
        var split = id.Split('/');
        if (document.Profile || split.Length != 3 || split[0] != "slots" || split[2] != "index"
            || !int.TryParse(split[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return false;
        var rows = document.Root.Children("equipped_inventory_list").ToArray();
        if ((uint)index >= (uint)rows.Length) return false;
        _ = document.DerivedInteger(id, "Equipment slot " + index + " inventory index",
            () => document.Root.Children("equipped_inventory_list").ElementAt(index), "inventory_list_index", int.MaxValue);
        return true;
    }

    private static bool TryAccount(GearboxDocument document, string id)
    {
        var split = id.Split('/');
        if (split.Length != 3 || split[0] != "account") return false;
        var role = split[1];
        if (document.Root.Schema.Wonderlands)
        {
            if (document.Profile ? role != "myth" : role is not ("hero" or "myth-mirror")) return false;
        }
        else if (document.Profile ? role != "profile" : role is not ("legacy" or "character")) return false;
        Func<ProtoMessage?> parent = () => split[1] switch
        {
            "hero" => document.Root.Child("hero_points_save_data"),
            "profile" => document.Root.Child("guardian_rank"),
            "legacy" => document.Root.Child("guardian_rank"),
            "character" => document.Root.Child("guardian_rank_character_data"),
            "myth" => document.Root.Child("player_prestige"),
            "myth-mirror" => document.Root.Child("profile_cloud_data")?.Child("player_prestige"),
            _ => null,
        };
        var name = split[2];
        if (parent() is not { } current || !IntegerField(current, name)) return false;
        var (label, maximum, description, warning) = AccountPresentation(document, role, name, current);
        _ = document.AccountNumber(id, label, () => parent()!, name, maximum,
            writable: name != "guardian_reward_random_seed", description: description, warning: warning);
        return true;
    }

    private static (string Label, long Maximum, string Description, string Warning) AccountPresentation(
        GearboxDocument document, string role, string name, ProtoMessage parent)
    {
        if (document.Root.Schema.Wonderlands && role == "hero")
        {
            var record = document.World.Find("hero:" + name);
            return (record?.Name ?? name, Math.Max(parent.Integer(name), 30), record?.AttributionText + " " + SerialWrites.Warning,
                "Stored raw stat; backstory, budget and Myth bonuses are not recomputed. " + SerialWrites.Warning);
        }
        if (document.Root.Schema.Wonderlands && name == "prestige_experience")
            return ("Stored Myth XP", Math.Max(parent.Integer(name), 1_000_000), AccountDescription, SerialWrites.Warning);
        if (document.Root.Schema.Wonderlands && name == "last_upgrade_tree_index")
            return ("Last upgrade tree index (raw)", 3, AccountDescription, SerialWrites.Warning);
        var label = name switch
        {
            "guardian_rank" => "Stored rank",
            "guardian_experience" => "Legacy XP",
            "new_guardian_experience" => "New XP",
            "available_tokens" or "guardian_available_tokens" => "Available tokens",
            "is_rank_system_enabled" => "Rank system enabled (raw)",
            "guardian_reward_random_seed" => "Reward random seed (raw)",
            _ => name.Replace('_', ' '),
        };
        return (label, name == "is_rank_system_enabled" ? 1 : Math.Max(parent.Integer(name), 1_000_000), AccountDescription, SerialWrites.Warning);
    }

    private const string AccountDescription = "Existing raw stored value; observed range plus conservative editor bound. Point budgets, mirror precedence and derived rewards are not inferred. " + SerialWrites.Warning;

    private static bool TryItem(GearboxDocument document, string id)
    {
        var split = id.Split('/');
        var requestedList = split.Length == 3 && split[0] is GearboxInventory.Backpack or GearboxInventory.Bank or GearboxInventory.LostLoot ? split[0] : null;
        var itemId = split.Length == 2 ? split[0] : requestedList is not null ? split[1] : null;
        var property = split.Length == 2 ? split[1] : split.Length == 3 ? split[2] : null;
        if (itemId is null || property is null) return false;
        if (property == "presence")
        {
            try
            {
                _ = document.Inventory.Find(itemId);
                if (requestedList is not null && document.Inventory.Find(itemId).List != requestedList) return false;
                _ = document.Presence(id, () => FieldValue.Of(1));
                return true;
            }
            catch (SaveFormatException) { return false; }
        }
        if (property is not ("level" or "rerolls" or "chaos" or "mark" or "flags")) return false;
        InventoryEntry entry;
        try { entry = document.Inventory.Find(itemId); } catch (SaveFormatException) { return false; }
        if (requestedList is not null && entry.List != requestedList) return false;
        var info = document.Inventory.Info(entry.Id);
        if (property == "mark" && entry.Field.Message is { } message && GearboxItemMarks.Supported(message))
        {
            var label = "Mark (0 ordinary, 1 favourite, 2 junk)";
            Func<int> get = () => GearboxItemMarks.Get(document.Inventory.Find(entry.Id).Field.Message!);
            Action<int> set = value => GearboxItemMarks.Set(document.Inventory.Find(entry.Id).Field.Message!, value);
            if (requestedList is null) _ = document.ItemNumber(id, label, 0, 2, get, set);
            else
            {
                var canonical = entry.Id + "/mark";
                _ = document.ItemNumber(canonical, label, 0, 2, get, set);
                _ = document.DerivedItemNumber(id, canonical, label, 0, 2, get);
            }
            return true;
        }
        if (property == "flags" && entry.Field.Message is not null)
        {
            var label = "Item flags";
            Func<ProtoMessage> get = () => document.Inventory.Find(entry.Id).Field.Message!;
            if (requestedList is null)
                _ = document.Integer(id, label, get, "flags", long.MaxValue,
                    warning: "Raw item flags; favourite and junk state is preserved by the shared mark policy.");
            else
            {
                var canonical = entry.Id + "/flags";
                _ = document.Integer(canonical, label, get, "flags", long.MaxValue,
                    warning: "Raw item flags; favourite and junk state is preserved by the shared mark policy.");
                _ = document.DerivedItemNumber(id, canonical, label, 0, int.MaxValue,
                    () => checked((int)get().Integer("flags")));
            }
            return true;
        }
        var located = property == "level" ? info.LevelOffset : property == "rerolls" ? info.RerollOffset : info.ChaosOffset;
        if (located is null || property == "chaos" && !document.Root.Schema.Wonderlands) return false;
        var value = SerialWrites.Value(info, property);
        if (value is null) return false;
        var minimum = property == "level" ? 1 : 0;
        var maximum = SerialWrites.Maximum(document.Root.Schema.Wonderlands, property);
        Func<int> read = () => SerialWrites.Value(document.Inventory.Info(entry.Id), property)!.Value;
        Action<int> write = next => document.Inventory.SetSerial(entry.Id,
            SerialWrites.Set(document.Inventory.Find(entry.Id).Serial, document.Root.Schema.Wonderlands, property, next));
        if (requestedList is null) _ = document.ItemNumber(id, "Item " + property, minimum, maximum, read, write);
        else
        {
            var canonical = entry.Id + "/" + property;
            _ = document.ItemNumber(canonical, "Item " + property, minimum, maximum, read, write);
            _ = document.DerivedItemNumber(id, canonical, "Item " + property, minimum, maximum, read);
        }
        return true;
    }

    private static int NameLimit(GearboxDocument document)
    {
        var value = document.Root.Integer("name_character_limit");
        return value is > 0 and <= 128 ? (int)value : 32;
    }

    private static bool Has(ProtoMessage message, string name) => message.Fields.Any(field => field.Spec?.Name == name);
    private static bool IntegerField(ProtoMessage message, string name) => message.Fields.Any(field =>
        field.Spec?.Name == name && field.Value is ulong or long or uint or int);

    private static bool TryActionObservation(GearboxDocument document, string id)
    {
        if (id == "import/serial")
        {
            _ = document.ActionObservation(id, "Imported serial", new TextSpec { ReadOnly = true },
                () => FieldValue.Null, "Structural import payload; the validated serial is applied by the import action.");
            return true;
        }
        var split = id.Split('/');
        if (split.Length == 2 && split[1] == "count" && split[0] is GearboxInventory.Backpack or GearboxInventory.Bank or GearboxInventory.LostLoot)
        {
            var list = split[0];
            if (!document.Root.All(list).Any()) return false;
            _ = document.ActionObservation(id, list + " item count", new IntegerSpec(0, int.MaxValue),
                () => FieldValue.Of(document.Root.All(list).Count()),
                "Structural item cardinality; import, transfer, duplicate and removal actions own the write.");
            return true;
        }
        return false;
    }
}
