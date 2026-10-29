using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal static class PlayerStatsReader
{
    public static IReadOnlyDictionary<ulong, int> Read(CsavContainer container, CsavTree tree)
    {
        var nodes = tree.Nodes.Where(n => n.Name == "StatsSystem").ToArray();
        if (nodes.Length != 1)
        {
            throw new SaveFormatException("Attribute editing requires one verified StatsSystem package.");
        }

        var node = nodes[0];
        var bytes = ProgressionData.PackageBytes(container.Payload, node);
        var package = new RedPackage(bytes, false);
        if (package.Types.Count != 1 || package.Types[0] != "gameStatsStateMapStructure")
        {
            throw new SaveFormatException("Unverified StatsSystem root layout; attributes remain read-only.");
        }

        var root = package.Chunk(0);
        var keys = root.StructArray(root.Require("keys", "array:gameStatsObjectID"));
        var values = root.StructArray(root.Require("values", "array:gameSavedStatsData"));
        if (keys.Count != values.Count)
        {
            throw new SaveFormatException("StatsSystem key/value counts disagree.");
        }

        var players = keys.Select((key, index) => (key, index)).Where(pair =>
            pair.key.Enum("idType", "gameStatIDType", "Invalid") == "EntityID" &&
            BinaryData.UInt64(pair.key.Raw(pair.key.Require("entityHash", "Uint64")), 0) == 1).ToArray();
        if (players.Length != 1)
        {
            throw new SaveFormatException("Attribute editing requires a unique entity-1 stats entry.");
        }

        var player = values[players[0].index];
        var field = player.Require("forcedModifiersBuffer", "DataBuffer");
        var buffer = player.Raw(field);
        var length = BinaryData.Int32(buffer, 0);
        if (length != buffer.Length - 4)
        {
            throw new SaveFormatException("Unverified player forced-modifier buffer length.");
        }

        return ReadModifierOffsets(buffer, node.Offset + 8 + field.Offset);
    }

    private static IReadOnlyDictionary<ulong, int> ReadModifierOffsets(byte[] buffer, int bufferOffset)
    {
        var offsets = new Dictionary<ulong, int>();
        var position = 4;
        while (position < buffer.Length)
        {
            var name = BinaryData.PackedName(buffer, ref position);
            if (name != "gameConstantStatModifier" || BinaryData.Int32(buffer, position + 8) != 0)
            {
                throw new SaveFormatException("Attribute editing supports only verified additive constant player modifiers.");
            }

            var hash = BinaryData.UInt64(buffer, position);
            var value = BinaryPrimitives.ReadSingleLittleEndian(BinaryData.Slice(buffer, position + 12, 4));
            if (!float.IsFinite(value) || !offsets.TryAdd(hash, bufferOffset + position + 12))
            {
                throw new SaveFormatException("Ambiguous or invalid player stat modifiers; attributes remain read-only.");
            }

            position += 16;
        }

        return offsets;
    }

    public static ulong NameHash(string name)
    {
        const ulong basis = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = basis;
        foreach (var character in name)
        {
            hash = unchecked((hash ^ character) * prime);
        }

        return hash;
    }
}
