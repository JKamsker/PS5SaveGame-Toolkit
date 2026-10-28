using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record PlayerData(PlayerProgress Progress, PlayerInventory? Inventory)
{
    public string EntityId { get; init; } = "";
    public Sav3Node? AbilityManager { get; init; }
    public Sav3Node? Entity { get; init; }
    public static PlayerData? Read(WitcherContainer container, Sav3Directory directory, Sav3Tree tree)
    {
        var data = container.Payload;
        // Writing is calibrated to the supplied next-gen PS5 schema, not every SNFH save.
        if (BinaryData.Int32(data, 4) != 64 || BinaryData.Int32(data, 8) != 23 || BinaryData.Int32(data, 12) != 163)
        {
            return null;
        }

        var players = tree.Nodes.Where(n => n.Tag == "BS" && n.Name == "Player").ToArray();
        if (players.Length != 1)
        {
            return null;
        }

        var playerIds = Children(players[0]).Where(n => n.Name == "id" && n.Type == "IdTag" && n.ValueSize == 17).ToArray();
        if (playerIds.Length != 1)
        {
            return null;
        }

        var playerId = data.AsMemory(playerIds[0].ValueOffset, 17);
        var owners = tree.Nodes.Where(n => n.Tag == "BS" && n.Name == "entityData" &&
            Children(n).Any(c => c.Name == "idTag" && c.Type == "IdTag" && c.ValueSize == 17 &&
                data.AsSpan(c.ValueOffset, 17).SequenceEqual(playerId.Span))).ToArray();
        if (owners.Length != 1)
        {
            return null;
        }

        var archives = Children(owners[0]).Where(n => n.Tag == "SS").ToArray();
        if (archives.Length != 1)
        {
            return null;
        }

        var archive = archives[0];
        var roots = Children(archive).ToArray();
        var versions = roots.Where(n => n.Tag == "SXAP").ToArray();
        if (versions.Length != 1 || !data.AsSpan(versions[0].Offset + 4, 12).SequenceEqual(data.AsSpan(4, 12)))
        {
            return null;
        }

        var entities = roots.Where(n => n.Tag == "BLCK" && n.Name == "Entity").ToArray();
        if (entities.Length != 1)
        {
            return null;
        }

        var managers = Children(entities[0]).Where(n => n.Tag == "PORP" && n.Name == "levelManager" && n.Type == "handle:W3LevelManager").ToArray();
        if (managers.Length != 1)
        {
            return null;
        }

        var progress = new PlayerProgress(managers[0], data, directory.Names);
        var tails = roots.Where(n => n.Tag == "Opaque" && n.Offset + n.Size == archive.Offset + archive.Size).ToArray();
        var inventory = tails.Length == 1 ? PlayerInventory.Read(tails[0], data, directory.Names) : null;
        var abilities = Children(entities[0]).Where(n => n.Tag == "PORP" && n.Name == "abilityManager" && n.Type == "handle:W3AbilityManager").ToArray();
        return new PlayerData(progress, inventory) {
            EntityId = Convert.ToHexString(playerId.Span),
            AbilityManager = abilities.Length == 1 ? abilities[0] : null,
            Entity = entities[0],
        };

        IEnumerable<Sav3Node> Children(Sav3Node node) => node.Children.Select(id => tree.Nodes[id]);
    }

    public IEnumerable<Sav3Patch> Patches(Sav3Directory directory, WitcherContainer container) =>
        Progress.Patches(directory, container).Concat(Inventory?.Patches() ?? []);

    public void Verify(PlayerData? reopened)
    {
        if (reopened is null || reopened.Progress.Level != Progress.Level || reopened.Progress.Experience != Progress.Experience ||
            reopened.Progress.AbilityPoints != Progress.AbilityPoints || reopened.Progress.UsedExperience != Progress.UsedExperience ||
            reopened.Progress.UsedAbilityPoints != Progress.UsedAbilityPoints)
        {
            throw new SaveFormatException("Edited player progression did not survive serialization.");
        }

        if (Inventory is not null && (reopened.Inventory is null || !Inventory.Items.Select(i => (i.Id, i.Name, i.Quantity))
            .SequenceEqual(reopened.Inventory.Items.Select(i => (i.Id, i.Name, i.Quantity)))))
        {
            throw new SaveFormatException("Edited player inventory did not survive serialization.");
        }

        if (Inventory is not null)
        {
            foreach (var (before, after) in Inventory.Items.Zip(reopened.Inventory!.Items))
            {
                if (before.Flags != after.Flags || BitConverter.SingleToInt32Bits(before.Durability) != BitConverter.SingleToInt32Bits(after.Durability) || before.Slot != after.Slot ||
                    before.OpaquePrefix != after.OpaquePrefix || !before.PrefixNames.SequenceEqual(after.PrefixNames) ||
                    !before.Tags.SequenceEqual(after.Tags) || !before.Attributes.SequenceEqual(after.Attributes))
                {
                    throw new SaveFormatException("Unedited inventory metadata changed during serialization.");
                }
            }
        }
    }
}
