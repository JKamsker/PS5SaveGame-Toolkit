using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Witcher3.Tests;

/// <summary>F1 hook: identity paths deliberately exclude record positions, dictionary indices and localized names.</summary>
internal sealed class WitcherSemanticDecoder : ISaveDecoder
{
    public string Id => "witcher3-native-identities";
    public bool CanDecode(ISaveFile file) => new Witcher3Plugin().Probe(file).Score > ProbeScore.Fallback;

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes) { throw new InvalidDataException("File exceeds decoder limit."); }
        var container = WitcherContainer.Parse(file.ReadAllBytes());
        var directory = Sav3Directory.Parse(container);
        var tree = Sav3Tree.Parse(container.Payload, directory);
        var player = PlayerData.Read(container, directory, tree) ?? throw new SaveFormatException("No supported player identity.");
        var builder = new SemanticTreeBuilder(Id, limits);
        void Add(string path, string kind, object value) => builder.Add(path, kind, Convert.ToString(value, CultureInfo.InvariantCulture)!);
        var root = $"entity/{player.EntityId}";
        Add("schema", "tuple", $"{BinaryData.Int32(container.Payload, 4)}/{BinaryData.Int32(container.Payload, 8)}/{BinaryData.Int32(container.Payload, 12)}");
        Add("container/expandedBytes", "length", container.Payload.Length);
        Add("directory/nameCount", "count", directory.Names.Count);
        Add("directory/variableCount", "count", directory.Entries.Count);
        Add("directory/namePointer", "offset", BinaryData.Int32(container.Payload, directory.VariableTableOffset - 10));
        Add($"{root}/level", "integer", player.Progress.Level);
        Add($"{root}/xp", "integer", player.Progress.Experience);
        Add($"{root}/abilityPoints", "integer", player.Progress.AbilityPoints);
        Add($"{root}/inventory/count", "count", player.Inventory?.Items.Count ?? 0);
        foreach (var item in player.Inventory?.Items ?? [])
        {
            var path = $"{root}/inventory/{item.Id}";
            Add($"{path}/name", "name", item.Name);
            Add($"{path}/quantity", "integer", item.Quantity);
            Add($"{path}/flags", "uint32", item.Flags);
            Add($"{path}/slot", "name", item.Slot);
            Add($"{path}/durability", "float32", item.Durability.ToString("R", CultureInfo.InvariantCulture));
            Add($"{path}/length", "length", item.Size);
            Add($"{path}/opaquePrefix", "sha256", item.OpaqueHash);
            Add($"{path}/prefixNames", "names", string.Join("|", item.PrefixNames));
            Add($"{path}/tags", "names", string.Join("|", item.Tags));
            Add($"{path}/attributes", "raw-attributes", string.Join("|", item.Attributes.Select(a => $"{a.Name}:{a.RawValue:X8}:{a.Type}")));
        }

        if (player.AbilityManager is { } manager)
        {
            foreach (var array in AbilityReader.Read(manager, container.Payload, directory.Names))
            {
                Add($"{root}/abilities/{array.Name}/count", "count", array.Entries.Count);
                foreach (var entry in array.Entries)
                {
                    foreach (var property in entry.Record.Properties)
                    {
                        // Resolve name-index values before comparing; unknown binary properties retain length/hash evidence.
                        var node = new Sav3Node(0, property.Offset, property.Bytes.Length, "property", property.Name,
                            property.Type, property.Offset + 8, property.Value.Length);
                        var value = property.Type.StartsWith('E') || property.Type == "CName"
                            ? AbilityReader.NameValue(property, directory.Names)
                            : ScalarReader.Read(node, container.Payload, directory.Names)?.Value.ToString() ??
                                $"{property.Value.Length} bytes SHA-256 {Convert.ToHexString(SHA256.HashData(property.Value))}";
                        Add($"{root}/abilities/{array.Name}/{entry.Id}/{property.Name}", property.Type, value);
                    }
                }
            }
        }

        foreach (var card in GwentReader.Read(tree, container.Payload) ?? [])
        {
            Add($"gwent/collection/{card.Index}/copies", "integer", card.Copies);
        }

        return builder.Build();
    }
}
