using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>Public authored fixture seam for the browser runner. Expected bytes use explicit protobuf edits, not actions.</summary>
public static class GearboxBrowserFixture
{
    private static readonly string[] Actions = ["import", "profile-import", "deposit", "withdraw", "claim"];
    public static GearboxBrowserFixtureData Create(bool wonderlands)
    {
        var files = ((ISaveSet)ActionEffectData.Set(wonderlands)).AllFiles
            .ToDictionary(file => file.RelativePath, file => file.ReadAllBytes(), StringComparer.Ordinal);
        var serial = ActionEffectData.Serial(wonderlands);
        return new(files, Convert.ToBase64String(serial), Actions
            .ToDictionary(action => action, action => Expected(files, wonderlands, serial, action), StringComparer.Ordinal));
    }

    private static Dictionary<string, byte[]> Expected(Dictionary<string, byte[]> files, bool wonderlands, byte[] serial, string action)
    {
        var result = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
        var characterPath = files.Keys.Single(path => path.EndsWith("/1", StringComparison.Ordinal));
        var profilePath = files.Keys.Single(path => path.EndsWith("/profile", StringComparison.Ordinal));
        var character = GearboxContainer.Parse(files[characterPath], ActionEffectData.Title(wonderlands), false);
        var profile = GearboxContainer.Parse(files[profilePath], ActionEffectData.Title(wonderlands), true);
        var hero = character.Decode();
        var account = profile.Decode();
        if (action is "import" or "profile-import")
            Receive(action == "import" ? hero : account,
                action == "import" ? GearboxInventory.Backpack : GearboxInventory.Bank,
                new ProtoField(1, 2, null, [], serial, null));
        else
        {
            var source = action == "deposit" ? hero : account;
            var destination = action == "deposit" ? account : hero;
            var sourceList = action == "deposit" ? GearboxInventory.Backpack
                : action == "withdraw" ? GearboxInventory.Bank : GearboxInventory.LostLoot;
            var targetList = action == "deposit" ? GearboxInventory.Bank : GearboxInventory.Backpack;
            var removed = source.All(sourceList).First();
            Receive(destination, targetList, removed);
            source.Remove(removed);
            if (action == "deposit")
                foreach (var slot in hero.Children("equipped_inventory_list"))
                {
                    var index = slot.Integer("inventory_list_index");
                    slot.SetInteger("inventory_list_index", index == 0 ? -1 : index > 0 ? index - 1 : index);
                }
        }
        result[characterPath] = character.Encode(hero);
        result[profilePath] = profile.Encode(account);
        return result;
    }

    private static void Receive(ProtoMessage destination, string list, ProtoField source)
    {
        var serial = InventoryEntry.SerialOf(source);
        var spec = destination.Schema.Spec(destination.Type, list);
        ProtoMessage? item = null;
        if (destination.Schema.IsMessage(spec.Type))
        {
            item = source.Message?.Clone() ?? new ProtoMessage(destination.Schema, spec.Type, []);
            if (source.Message is null)
            {
                item.SetInteger("flags", 1);
                item.Add(new ProtoField(destination.Schema.Tag(spec.Type, "item_serial_number"), 2,
                    destination.Schema.Spec(spec.Type, "item_serial_number"), [], serial, null));
            }
            if (list == GearboxInventory.Backpack)
                item.SetInteger("pickup_order_index", destination.Children(list).Max(item => item.Integer("pickup_order_index")) + 1);
        }
        destination.Add(new ProtoField(destination.Schema.Tag(destination.Type, list), 2, spec, [], serial, item));
    }
}

public sealed record GearboxBrowserFixtureData(Dictionary<string, byte[]> Files, string Serial,
    Dictionary<string, Dictionary<string, byte[]>> Expected);
