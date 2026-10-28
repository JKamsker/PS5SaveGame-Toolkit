using System.Globalization;
using System.Security.Cryptography;
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
        bool Add(string path, string kind, object value)
        {
            if (path.Count(c => c == '/') > sink.MaxDepth)
            {
                sink.MarkTruncated($"Native identity depth limit {sink.MaxDepth} reached at '{path}'.");
                return false;
            }

            return sink.Add(path, kind, Convert.ToString(value, CultureInfo.InvariantCulture)!);
        }
        var root = $"entity/{player.EntityId}";
        if (!Add("schema", "tuple", $"{BinaryData.Int32(container.Payload, 4)}/{BinaryData.Int32(container.Payload, 8)}/{BinaryData.Int32(container.Payload, 12)}")) { return; }
        if (!Add("container/expandedBytes", "length", container.Payload.Length)) { return; }
        if (!Add("directory/nameCount", "count", directory.Names.Count)) { return; }
        if (!Add("directory/variableCount", "count", directory.Entries.Count)) { return; }
        if (!Add("directory/namePointer", "offset", BinaryData.Int32(container.Payload, directory.VariableTableOffset - 10))) { return; }
        if (!Add($"{root}/level", "integer", player.Progress.Level)) { return; }
        if (!Add($"{root}/xp", "integer", player.Progress.Experience)) { return; }
        if (!Add($"{root}/abilityPoints", "integer", player.Progress.AbilityPoints)) { return; }
        if (!Add($"{root}/inventory/count", "count", player.Inventory?.Items.Count ?? 0)) { return; }
        foreach (var item in player.Inventory?.Items ?? [])
        {
            var path = $"{root}/inventory/{item.Id}";
            if (!Add($"{path}/name", "name", item.Name)) { return; }
            if (!Add($"{path}/quantity", "integer", item.Quantity)) { return; }
            if (!Add($"{path}/flags", "uint32", item.Flags)) { return; }
            if (!Add($"{path}/slot", "name", item.Slot)) { return; }
            if (!Add($"{path}/durability", "float32", item.Durability.ToString("R", CultureInfo.InvariantCulture))) { return; }
            if (!Add($"{path}/length", "length", item.Size)) { return; }
            if (!Add($"{path}/opaquePrefix", "sha256", item.OpaqueHash)) { return; }
            if (!Add($"{path}/prefixNames", "names", string.Join("|", item.PrefixNames))) { return; }
            if (!Add($"{path}/tags", "names", string.Join("|", item.Tags))) { return; }
            if (!Add($"{path}/attributes", "raw-attributes", string.Join("|", item.Attributes.Select(a => $"{a.Name}:{a.RawValue:X8}:{a.Type}")))) { return; }
        }

        if (player.AbilityManager is { } manager)
        {
            foreach (var array in AbilityReader.Read(manager, container.Payload, directory.Names))
            {
                if (!Add($"{root}/abilities/{array.Name}/count", "count", array.Entries.Count)) { return; }
                foreach (var entry in array.Entries)
                {
                    if (entry.Record.Properties.Count > 0 && entry.Id.StartsWith("unnamed-slot-", StringComparison.Ordinal))
                    {
                        sink.MarkTruncated($"{array.Name}/{entry.Id} has only a positional identity; record alignment is unproven.");
                    }
                    foreach (var property in entry.Record.Properties)
                    {
                        // Resolve name-index values before comparing; unknown binary properties retain length/hash evidence.
                        var node = new Sav3Node(0, property.Offset, property.Bytes.Length, "property", property.Name,
                            property.Type, property.Offset + 8, property.Value.Length);
                        var value = property.Type.StartsWith('E') || property.Type == "CName"
                            ? AbilityReader.NameValue(property, directory.Names)
                            : ScalarReader.Read(node, container.Payload, directory.Names)?.Value.ToString() ??
                                $"{property.Value.Length} bytes SHA-256 {Convert.ToHexString(SHA256.HashData(property.Value))}";
                        if (!Add($"{root}/abilities/{array.Name}/{entry.Id}/{property.Name}", property.Type, value)) { return; }
                    }
                }
            }
        }

        foreach (var card in GwentReader.Read(tree, container.Payload) ?? [])
        {
            if (!Add($"gwent/collection/{card.Index}/copies", "integer", card.Copies)) { return; }
        }
    }
}
