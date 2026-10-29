using System.Globalization;
using Google.Protobuf;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Read each stored mirror independently; no reconciliation direction or computed rank.</summary>
internal sealed class GearboxAccountViews(GearboxDocument document, GearboxLazyViews lazy)
{
    private static readonly string[] RewardScalars = ["num_tokens", "is_enabled"];
    public IReadOnlyList<ViewNode> Build()
    {
        List<ViewNode> nodes = [new TextNode("Account scalar/allocation edits are available only in an explicit character + profile group. Differences do not establish which file wins on load. Missing records are not created; edits never infer mirror precedence or point budgets. " + SerialWrites.Warning) { Tone = Tone.Warning }];
        if (document.Root.Schema.Wonderlands) Wonderlands(nodes);
        else Borderlands(nodes);
        return nodes;
    }

    private void Borderlands(List<ViewNode> nodes)
    {
        if (document.Profile) AddGuardian(nodes, "account/profile", "Profile Guardian state", () => document.Root.Child("guardian_rank"));
        else
        {
            AddGuardian(nodes, "account/legacy", "Character legacy Guardian state", () => document.Root.Child("guardian_rank"));
            AddGuardian(nodes, "account/character", "Character Guardian mirror", () => document.Root.Child("guardian_rank_character_data"));
        }
    }

    private void AddGuardian(List<ViewNode> nodes, string id, string title, Func<ProtoMessage?> get)
    {
        var current = get();
        if (current is null) { nodes.Add(new TextNode(title + ": record absent (preserved).")); return; }
        var fields = new List<ViewNode>();
        foreach (var (name, label) in new[] { ("guardian_rank", "Stored rank"), ("guardian_experience", "Legacy XP"),
            ("new_guardian_experience", "New XP"), ("available_tokens", "Available tokens"), ("guardian_available_tokens", "Available tokens"),
            ("is_rank_system_enabled", "Rank system enabled (raw)"), ("guardian_reward_random_seed", "Reward random seed (raw)") })
        {
            if (!Has(current, name)) continue;
            fields.Add(AccountNumber(id + "/" + name, label, () => get()!, name,
                name == "is_rank_system_enabled" ? 1 : Math.Max(current.Integer(name), 1_000_000), name != "guardian_reward_random_seed"));
        }
        if (Has(current, "rank_rewards")) fields.Add(lazy.Register(id + "/rewards", "Guardian reward allocations (raw assets)",
            () => [GuardianTable(get, id, "rank_rewards", "reward_data_path").Build()]));
        if (Has(current, "rank_perks")) fields.Add(lazy.Register(id + "/perks", "Guardian perks (raw assets)",
            () => [GuardianTable(get, id, "rank_perks", "perk_data_path").Build()]));
        nodes.Add(new SectionNode(title, fields));
    }

    private GearboxTable GuardianTable(Func<ProtoMessage?> get, string id, string list, string asset) =>
        new(id + "/" + list, list == "rank_rewards" ? "Guardian reward allocations" : "Guardian perks",
            [new("asset", "Raw asset"), new("num_tokens", "Tokens invested", IntegerSpec.Int32), new("is_enabled", "Enabled (raw)", IntegerSpec.Int32)],
            () => get()!.Children(list).Select((entry, position) =>
            {
                var key = $"{id}/{list}/{position}";
                var values = new Dictionary<string, string> { ["asset"] = entry.Text(asset) };
                foreach (var field in RewardScalars) values[field] = Has(entry, field) ? entry.Integer(field).ToString(CultureInfo.InvariantCulture) : "Absent";
                return new GearboxQueryRow(key, values, () =>
                {
                    var cells = new Dictionary<string, ViewNode> { ["asset"] = document.InspectionField(key + "/asset", "Raw asset", entry.Text(asset), Confidence.Unknown, null) };
                    foreach (var field in RewardScalars)
                        cells[field] = Has(entry, field) ? AccountNumber(key + "/" + field, field,
                            () => get()!.Children(list).ElementAt(position), field, field == "is_enabled" ? 1 : Math.Max(entry.Integer(field), 1000)) : new TextNode("Absent");
                    return new TableRow(key, cells);
                });
            }));

    private void Wonderlands(List<ViewNode> nodes)
    {
        if (!document.Profile && document.Root.Child("hero_points_save_data") is { } hero)
        {
            var fields = new List<ViewNode>();
            foreach (var name in new[] { "strength", "dexterity", "intelligence", "wisdom", "constitution", "luck" })
            {
                if (!Has(hero, name)) continue;
                var record = document.World.Find("hero:" + name);
                fields.Add(AccountNumber("account/hero/" + name, record?.Name ?? name,
                    () => document.Root.Child("hero_points_save_data")!, name, Math.Max(hero.Integer(name), 30),
                    description: record?.AttributionText + " " + SerialWrites.Warning,
                    warning: "Stored raw stat; backstory, budget and Myth bonuses are not recomputed. " + SerialWrites.Warning));
            }
            if (Has(hero, "player_aspect_data_path")) fields.Add(document.World.Label("account/hero/backstory", "Backstory", hero.Text("player_aspect_data_path")));
            nodes.Add(new SectionNode("Character Hero stats", fields));
        }
        Func<ProtoMessage?> get = document.Profile ? () => document.Root.Child("player_prestige")
            : () => document.Root.Child("profile_cloud_data")?.Child("player_prestige");
        var current = get();
        var id = document.Profile ? "account/myth" : "account/myth-mirror";
        if (current is null) { nodes.Add(new TextNode("Myth record absent (preserved); no rank or point budget inferred.")); return; }
        var values = new List<ViewNode>();
        foreach (var (name, label) in new[] { ("prestige_experience", "Stored Myth XP"), ("last_upgrade_tree_index", "Last upgrade tree index (raw)") })
            if (Has(current, name)) values.Add(AccountNumber(id + "/" + name, label, () => get()!, name,
                name == "last_upgrade_tree_index" ? 3 : Math.Max(current.Integer(name), 1_000_000)));
        values.Add(lazy.Register(id + "/allocations", "Myth indexed allocations", () => [new TextNode("Upstream index labels; PS5 allocation rotation/caps unproven. No computed Myth rank."), MythTable(get, id).Build()]));
        nodes.Add(new SectionNode(document.Profile ? "Profile Myth state" : "Character cloud Myth mirror", values));
    }

    private GearboxTable MythTable(Func<ProtoMessage?> get, string id) => new(id + "/allocations", "Myth indexed allocations",
        [new("index", "Array index", IntegerSpec.Int32), new("name", "Upstream stat label"), new("points", "Stored points", IntegerSpec.Int32)],
        () => Integers(get()!, "points_spent_by_index_order").Select((value, position) =>
        {
            var record = document.World.Find("myth:" + position.ToString(CultureInfo.InvariantCulture));
            var key = $"{id}/{position}";
            var values = new Dictionary<string, string> { ["index"] = position.ToString(CultureInfo.InvariantCulture),
                ["name"] = record?.Name ?? "Unknown index", ["points"] = value.ToString(CultureInfo.InvariantCulture) };
            return new GearboxQueryRow(key, values, () => new(key, new Dictionary<string, ViewNode>
            {
                ["index"] = new TextNode(values["index"]),
                ["name"] = document.InspectionField(key + "/name", "Stat", values["name"], record is null ? Confidence.Unknown : Confidence.Inferred, record?.AttributionText),
                ["points"] = MythPoints(get, id, position, value,
                    SerialDatabase.For(true).Find(GearboxWorldCatalog.CatalogId, "myth:" + position.ToString(CultureInfo.InvariantCulture))?.Attributes.GetValueOrDefault("upstreamCap"), record?.AttributionText),
            }));
        }));

    public IReadOnlyList<QueryableCollection> Collections => Tables.Select(t => t.Collection).ToArray();
    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query) => Tables.Single(t => t.Collection.Id == id).Query(query);

    private IEnumerable<GearboxTable> Tables
    {
        get
        {
            if (document.Root.Schema.Wonderlands)
            {
                Func<ProtoMessage?> get = document.Profile ? () => document.Root.Child("player_prestige")
                    : () => document.Root.Child("profile_cloud_data")?.Child("player_prestige");
                if (get() is not null) yield return MythTable(get, document.Profile ? "account/myth" : "account/myth-mirror");
                yield break;
            }
            var parents = document.Profile ? new[] { ("account/profile", "guardian_rank") }
                : [("account/legacy", "guardian_rank"), ("account/character", "guardian_rank_character_data")];
            foreach (var (id, name) in parents)
            {
                Func<ProtoMessage?> get = () => document.Root.Child(name);
                if (get() is not { } parent) continue;
                if (Has(parent, "rank_rewards")) yield return GuardianTable(get, id, "rank_rewards", "reward_data_path");
                if (Has(parent, "rank_perks")) yield return GuardianTable(get, id, "rank_perks", "perk_data_path");
            }
        }
    }

    internal static IEnumerable<int> Integers(ProtoMessage message, string name)
    {
        foreach (var field in message.All(name))
        {
            if (field.Value is ulong value) yield return unchecked((int)value);
            else if (field.Value is byte[] bytes)
            {
                using var reader = new CodedInputStream(bytes);
                while (!reader.IsAtEnd) yield return reader.ReadInt32();
            }
        }
    }

    internal static bool TryIntegerAt(ProtoMessage message, string name, int index, out int value, bool bounded = true)
    {
        value = 0;
        var ordinal = 0;
        var visited = 0;
        foreach (var field in message.Fields)
        {
            if (field.Spec?.Name != name) continue;
            if (bounded && ++visited > GearboxInventory.DirectLookupWorkLimit) return false;
            if (field.Value is ulong scalar)
            {
                if (ordinal++ == index) { value = unchecked((int)scalar); return true; }
                continue;
            }
            if (field.Value is not byte[] bytes) return false;
            using var reader = new CodedInputStream(bytes);
            while (!reader.IsAtEnd)
            {
                if (bounded && ++visited > GearboxInventory.DirectLookupWorkLimit) return false;
                if (ordinal++ == index) { value = reader.ReadInt32(); return true; }
                reader.ReadInt32();
            }
        }
        return false;
    }

    private FieldNode AccountNumber(string id, string label, Func<ProtoMessage> get, string name, long maximum,
        bool writable = true, string? description = null, string? warning = null) =>
        document.AccountNumber(id, label, get, name, maximum, writable, description, warning);

    private FieldNode MythPoints(Func<ProtoMessage?> get, string id, int position, int value, string? upstreamCap, string? attribution)
    {
        if (!document.Coordinated || !int.TryParse(upstreamCap, out var cap) || value < 0)
            return document.InspectionField($"{id}/{position}/points", "Stored points", value.ToString(CultureInfo.InvariantCulture), Confidence.Inferred, attribution);
        var maximum = cap == 0 ? Math.Max(value, 1000) : cap;
        var description = (attribution is null ? "" : attribution + " ") +
            "Raw allocation; upstream cap is an editor policy. XP/rank/rotation and point budgets are not automatically reconciled. " + SerialWrites.Warning;
        return document.MythPoints($"{id}/{position}/points", get, position, maximum, description);
    }

    private static bool Has(ProtoMessage message, string name) => message.Fields.Any(f => f.Spec?.Name == name);
}
