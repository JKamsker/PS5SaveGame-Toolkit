using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Opening pool values are document-owned and deliberately excluded from undo snapshots.</summary>
internal sealed class GearboxAmmoRestore(ProtoMessage opening, bool profile)
{
    internal const int Maximum = 100_000;
    internal const string ActionId = "ammo/restore-imported";
    internal const string Warning = GearboxWriteWarnings.ConsoleValidation +
        " Restore imported uses this workspace's immutable opening amounts, only when lower. It cannot recover ammo spent before import. " +
        "The game may clamp ammo to the capacity provided by its SDU. Pool paths, missing pools and SDUs are unchanged.";
    private readonly Pool[] _opening = profile ? [] : opening.Children("resource_pools")
        .Select(m => new Pool(m.Text("resource_path"), m.Float("amount"), m.All("amount").Count() == 1 && m.All("resource_path").Count() == 1)).ToArray();

    internal void Plan(GearboxDocument document, ActionScope scope, List<PlannedChange> changes, List<SkippedActionRow> skipped)
    {
        if (document.Profile) throw new SaveFormatException("Ammo restore requires a character document.");
        if (scope.QueryScope is { } query && (query.CollectionId != "ammo" || scope.RowKeys is null))
            throw new SaveFormatException("Resolve an ammo collection selection before restoring pools.");
        var current = document.Root.Children("resource_pools").ToArray();
        var keys = current.Select((_, i) => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        var selected = scope.RowKeys?.ToHashSet(StringComparer.Ordinal);
        if (selected is not null && (selected.Count != scope.RowKeys!.Count || !selected.IsSubsetOf(keys)))
            throw new SaveFormatException("The selection contains stale, foreign or duplicate ammo rows.");
        var database = SerialDatabase.For(document.Root.Schema.Wonderlands);
        var openingCounts = _opening.GroupBy(p => p.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var counts = current.GroupBy(p => p.Text("resource_path"), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        for (var i = 0; i < current.Length; i++)
        {
            if (selected is not null && !selected.Contains(keys[i])) continue;
            var pool = current[i];
            var path = pool.Text("resource_path");
            if (i >= _opening.Length || _opening[i].Path != path || !_opening[i].HasAmount || pool.All("amount").Count() != 1 || pool.All("resource_path").Count() != 1 ||
                database.Find(ItemCatalogs.Labels, path)?.Category != "ammo" || openingCounts.GetValueOrDefault(path) != 1 || counts[path] != 1 ||
                !Eligible(_opening[i].Amount) || !Eligible(pool.Float("amount")))
            {
                skipped.Add(new(keys[i], "Unknown, ambiguous, missing or out-of-policy ammo pool; opening and current amounts must both be finite and within 0–100,000."));
                continue;
            }
            var amount = pool.Float("amount");
            if (amount < _opening[i].Amount)
                changes.Add(new("ammo/" + keys[i], path + " imported ammo", FieldValue.Of(amount), FieldValue.Of(_opening[i].Amount)));
        }
    }

    internal static bool Eligible(float value) => float.IsFinite(value) && value is >= 0 and <= Maximum;
    private sealed record Pool(string Path, float Amount, bool HasAmount);
}
