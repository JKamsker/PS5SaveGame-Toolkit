using System.Globalization;
using Google.Protobuf;
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
            () => GuardianRows(get, id, "rank_rewards", "reward_data_path", 0)));
        if (Has(current, "rank_perks")) fields.Add(lazy.Register(id + "/perks", "Guardian perks (raw assets)",
            () => GuardianRows(get, id, "rank_perks", "perk_data_path", 0)));
        nodes.Add(new SectionNode(title, fields));
    }

    private IReadOnlyList<ViewNode> GuardianRows(Func<ProtoMessage?> get, string id, string list, string asset, int offset)
    {
        var entries = get()!.Children(list).ToArray();
        var rows = entries.Skip(offset).Take(GearboxLazyViews.PageSize).Select((entry, index) =>
        {
            var position = offset + index;
            var cells = new Dictionary<string, ViewNode> { ["asset"] = document.InspectionField($"{id}/{list}/{position}/asset", "Raw asset", entry.Text(asset), Confidence.Unknown, null) };
            foreach (var field in RewardScalars)
                cells[field] = Has(entry, field) ? AccountNumber($"{id}/{list}/{position}/{field}", field,
                    () => get()!.Children(list).ElementAt(position), field, field == "is_enabled" ? 1 : Math.Max(entry.Integer(field), 1000)) : new TextNode("Absent");
            return new TableRow($"{id}/{list}/{position}", cells);
        }).ToArray();
        List<ViewNode> result = [new TableNode([new("asset", "Raw asset"), new("num_tokens", "Tokens invested"), new("is_enabled", "Enabled (raw)")], rows)];
        if (offset + GearboxLazyViews.PageSize < entries.Length)
            result.Add(lazy.Register($"{id}/{list}/{offset + GearboxLazyViews.PageSize}", "More allocations", () => GuardianRows(get, id, list, asset, offset + GearboxLazyViews.PageSize)));
        return result;
    }

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
                    () => document.Root.Child("hero_points_save_data")!, name, Math.Max(hero.Integer(name), 30)) with
                { Description = record?.AttributionText + " " + SerialWrites.Warning, Warning = "Stored raw stat; backstory, budget and Myth bonuses are not recomputed. " + SerialWrites.Warning });
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
        values.Add(lazy.Register(id + "/allocations", "Myth indexed allocations", () => MythRows(get, id, 0)));
        nodes.Add(new SectionNode(document.Profile ? "Profile Myth state" : "Character cloud Myth mirror", values));
    }

    private IReadOnlyList<ViewNode> MythRows(Func<ProtoMessage?> get, string id, int offset)
    {
        var values = Integers(get()!, "points_spent_by_index_order").Skip(offset).Take(GearboxLazyViews.PageSize + 1).ToArray();
        var rows = values.Take(GearboxLazyViews.PageSize).Select((value, index) =>
        {
            var position = offset + index;
            var record = document.World.Find("myth:" + position.ToString(CultureInfo.InvariantCulture));
            return new TableRow($"{id}/{position}", new Dictionary<string, ViewNode>
            {
                ["index"] = new TextNode(position.ToString(CultureInfo.InvariantCulture)),
                ["name"] = document.InspectionField($"{id}/{position}/name", "Stat", record?.Name ?? "Unknown index", record is null ? Confidence.Unknown : Confidence.Inferred, record?.AttributionText),
                ["points"] = MythPoints(get, id, position, value,
                    SerialDatabase.For(true).Find(GearboxWorldCatalog.CatalogId, "myth:" + position.ToString(CultureInfo.InvariantCulture))?.Attributes.GetValueOrDefault("upstreamCap"), record?.AttributionText),
            });
        }).ToArray();
        List<ViewNode> nodes = [new TextNode("Upstream index labels; PS5 allocation rotation/caps unproven. No computed Myth rank."),
            new TableNode([new("index", "Array index"), new("name", "Upstream stat label"), new("points", "Stored points")], rows)];
        if (values.Length > GearboxLazyViews.PageSize)
            nodes.Add(lazy.Register($"{id}/allocations/{offset + GearboxLazyViews.PageSize}", "More Myth allocations", () => MythRows(get, id, offset + GearboxLazyViews.PageSize)));
        return nodes;
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

    private FieldNode AccountNumber(string id, string label, Func<ProtoMessage> get, string name, long maximum, bool writable = true) =>
        document.Integer(id, label, get, name, maximum, document.Coordinated && writable, SerialWrites.Warning) with
        { Confidence = Confidence.Inferred, Description = "Existing raw stored value; observed range plus conservative editor bound. Point budgets, mirror precedence and derived rewards are not inferred. " + SerialWrites.Warning };

    private FieldNode MythPoints(Func<ProtoMessage?> get, string id, int position, int value, string? upstreamCap, string? attribution)
    {
        if (!document.Coordinated || !int.TryParse(upstreamCap, out var cap) || value < 0)
            return document.InspectionField($"{id}/{position}/points", "Stored points", value.ToString(CultureInfo.InvariantCulture), Confidence.Inferred, attribution);
        var maximum = cap == 0 ? Math.Max(value, 1000) : cap;
        return document.ItemNumber($"{id}/{position}/points", "Stored points", 0, maximum,
            () => Integers(get()!, "points_spent_by_index_order").ElementAt(position),
            next => ProtoArrayEdits.Set(get()!, "points_spent_by_index_order", position, next)) with
        { Description = attribution + " Raw allocation; upstream cap is an editor policy. XP/rank/rotation and point budgets are not automatically reconciled. " + SerialWrites.Warning };
    }

    private static bool Has(ProtoMessage message, string name) => message.Fields.Any(f => f.Spec?.Name == name);
}
