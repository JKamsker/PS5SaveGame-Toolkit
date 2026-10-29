using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

internal static class PointTargetFixtures
{
    public static byte[] RewritePoints(Func<RedPackage, IReadOnlyList<PackageClass>, IEnumerable<byte[]>> rewrite)
    {
        var container = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        var node = Assert.Single(CsavTree.Parse(container).Nodes, n => n.Name == "ScriptableSystemsContainer");
        var package = new RedPackage(ProgressionData.PackageBytes(container.Payload, node), true);
        var playerIndex = Assert.Single(package.Types.Select((type, index) => (type, index)), p => p.type == "PlayerDevelopmentData").index;
        var player = package.Chunk(playerIndex);
        var records = rewrite(package, player.StructArray(player.Require("devPoints", "array:SDevelopmentPoints"))).ToArray();
        using var points = new MemoryStream();
        using (var writer = new BinaryWriter(points, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(records.Length);
            foreach (var record in records) writer.Write(record);
        }
        var rebuilt = package.Rebuild(playerIndex, player.Rebuild(new Dictionary<string, byte[]> { ["devPoints"] = points.ToArray() }));
        var replacement = new byte[rebuilt.Length + 8];
        BinaryPrimitives.WriteInt32LittleEndian(replacement, node.Id);
        BinaryPrimitives.WriteInt32LittleEndian(replacement.AsSpan(4), rebuilt.Length);
        rebuilt.CopyTo(replacement, 8);
        return container.Rebuild(container.Payload, new KeyValuePair<int, byte[]>(node.Id, replacement));
    }

    public static void AssertOnlyUnspentChanges(CsavContainer before, CsavContainer after,
        IReadOnlyDictionary<string, FieldValue> targets, int expectedGrowth = 24)
    {
        var oldTree = CsavTree.Parse(before);
        var newTree = CsavTree.Parse(after);
        var oldNode = Assert.Single(oldTree.Nodes, n => n.Name == "ScriptableSystemsContainer");
        var newNode = newTree.Nodes[oldNode.Id];
        // Both initially omitted balances acquire just one 8-byte header and one 4-byte scalar each.
        Assert.Equal(oldNode.Size + expectedGrowth, newNode.Size);
        Assert.Equal(before.Payload.AsSpan(0, oldNode.Offset).ToArray(), after.Payload.AsSpan(0, newNode.Offset).ToArray());
        Assert.Equal(before.Payload.AsSpan(oldNode.Offset + oldNode.Size).ToArray(), after.Payload.AsSpan(newNode.Offset + newNode.Size).ToArray());
        Assert.Equal(oldTree.Nodes.Select(n => (n.Id, n.Name, n.NextId, n.ChildId)),
            newTree.Nodes.Select(n => (n.Id, n.Name, n.NextId, n.ChildId)));
        var oldPackage = new RedPackage(ProgressionData.PackageBytes(before.Payload, oldNode), true);
        var newPackage = new RedPackage(ProgressionData.PackageBytes(after.Payload, newNode), true);
        Assert.Equal(oldPackage.Names, newPackage.Names);
        Assert.Equal(oldPackage.Types, newPackage.Types);
        for (var i = 0; i < oldPackage.Types.Count; i++)
        {
            var oldChunk = oldPackage.Chunk(i);
            var newChunk = newPackage.Chunk(i);
            if (oldPackage.Types[i] != "PlayerDevelopmentData")
            {
                Assert.Equal(oldChunk.Rebuild(new Dictionary<string, byte[]>()), newChunk.Rebuild(new Dictionary<string, byte[]>()));
                continue;
            }
            foreach (var field in oldChunk.Fields.Where(f => f.Name != "devPoints"))
                Assert.Equal(oldChunk.Raw(field), newChunk.Raw(newChunk.Require(field.Name, field.Type)));
            var oldPoints = oldChunk.StructArray(oldChunk.Require("devPoints", "array:SDevelopmentPoints"));
            var newPoints = newChunk.StructArray(newChunk.Require("devPoints", "array:SDevelopmentPoints"));
            Assert.Equal(oldPoints.Count, newPoints.Count);
            for (var j = 0; j < oldPoints.Count; j++)
            {
                var type = oldPoints[j].Enum("type", "gamedataDevelopmentPointType", "Attribute");
                if (!targets.TryGetValue(type, out var target))
                {
                    Assert.Equal(oldPoints[j].Rebuild(new Dictionary<string, byte[]>()), newPoints[j].Rebuild(new Dictionary<string, byte[]>()));
                    continue;
                }
                Assert.Equal(target.AsInt32(), newPoints[j].Int("unspent"));
                foreach (var field in oldPoints[j].Fields.Where(f => f.Name != "unspent"))
                    Assert.Equal(oldPoints[j].Raw(field), newPoints[j].Raw(newPoints[j].Require(field.Name, field.Type)));
                Assert.Equal(oldPoints[j].Int("spent"), newPoints[j].Int("spent"));
            }
        }
    }
}
