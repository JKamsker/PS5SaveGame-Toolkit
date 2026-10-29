using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>F12 evidence uses native identities; positional fallbacks are explicitly flagged as incomplete.</summary>
internal sealed class WitcherSemanticDecoder : ISaveDecoderHook
{
    public string Id => "witcher3-native-identities";
    public bool CanDecode(ISaveFile file) => new Witcher3Plugin().Probe(file).Score > ProbeScore.Fallback;

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > Math.Min(sink.MaxFileBytes, BinaryData.MaxFileSize)) { throw new InvalidDataException("File exceeds decoder limit."); }
        var container = WitcherContainer.Parse(file.ReadAllBytes());
        var directory = Sav3Directory.Parse(container);
        var tree = Sav3Tree.Parse(container.Payload, directory);
        var player = PlayerData.Read(container, directory, tree) ?? throw new SaveFormatException("No supported player identity.");
        var evidence = new NativeEvidence(sink);
        var root = $"entity/{player.EntityId}";
        if (!evidence.Add("schema", "tuple", $"{BinaryData.Int32(container.Payload, 4)}/{BinaryData.Int32(container.Payload, 8)}/{BinaryData.Int32(container.Payload, 12)}")) { return; }
        if (!evidence.Add("container/expandedBytes", "length", container.Payload.Length)) { return; }
        if (!evidence.Add("directory/nameCount", "count", directory.Names.Count)) { return; }
        if (!evidence.Add("directory/variableCount", "count", directory.Entries.Count)) { return; }
        if (!evidence.Add("directory/namePointer", "offset", BinaryData.Int32(container.Payload, directory.VariableTableOffset - 10))) { return; }
        if (!evidence.Add($"{root}/level", "integer", player.Progress.Level)) { return; }
        if (!evidence.Add($"{root}/xp", "integer", player.Progress.Experience)) { return; }
        if (!evidence.Add($"{root}/abilityPoints", "integer", player.Progress.AbilityPoints)) { return; }
        if (!evidence.Add($"{root}/inventory/count", "count", player.Inventory?.Items.Count ?? 0)) { return; }
        if (!evidence.Inventory(root, player.Inventory?.Items ?? [])) { return; }
        if (player.AbilityManager is { } manager && !evidence.Abilities(root,
            AbilityReader.Read(manager, container.Payload, directory.Names), container.Payload, directory.Names)) { return; }

        foreach (var card in GwentReader.Read(tree, container.Payload) ?? [])
        {
            if (!evidence.Add($"gwent/collection/{card.Index}/copies", "integer", card.Copies)) { return; }
        }
    }
}
