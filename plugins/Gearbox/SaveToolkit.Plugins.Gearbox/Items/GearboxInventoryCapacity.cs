using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Pure destination-growth checks shared by previews and committed inventory mutations.</summary>
internal static class GearboxInventoryCapacity
{
    internal static void Ensure(GearboxDocument document, string list, int additional)
    {
        if (additional < 0) throw new SaveFormatException("Destination growth cannot be negative.");
        if (list == GearboxInventory.Backpack && document.Root.Children(GearboxInventory.Backpack)
            .Select(m => m.Integer("pickup_order_index")).DefaultIfEmpty(0).Max() > int.MaxValue - additional)
            throw new SaveFormatException("Destination pickup order would overflow.");
        var count = document.Root.All(list).Count();
        var path = list == GearboxInventory.Backpack ? "Backpack" : list == GearboxInventory.Bank ? "Bank" : "LostLoot";
        var sdus = document.Root.Children(document.Profile ? "profile_sdu_list" : "sdu_list")
            .Where(m => m.Text("sdu_data_path").Contains("SDU_" + path + ".", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sdus.Length == 0 && !document.Root.Children(document.Profile ? "profile_sdu_list" : "sdu_list").Any())
        {
            var baseline = GearboxWritePolicies.Capacity(list, 0);
            if (count + additional <= baseline) return;
            throw new SaveFormatException($"No SDU record; destination growth is limited to the base-capacity policy ({baseline}).");
        }
        // Conservative editor policy from the pinned upstream SDU definitions; unknown paths reject growth.
        if (sdus.Length != 1) throw new SaveFormatException("Cannot establish the destination capacity from its existing SDU record.");
        var tier = sdus[0].Integer("sdu_level");
        var capacity = GearboxWritePolicies.Capacity(list, tier);
        var max = GearboxWritePolicies.SduMaximum(sdus[0].Text("sdu_data_path"), document.Root.Schema.Wonderlands);
        if (tier < 0 || max is null || tier > max || count + additional > capacity)
            throw new SaveFormatException($"Destination capacity policy ({capacity}) would be exceeded; no items were discarded.");
    }

}
