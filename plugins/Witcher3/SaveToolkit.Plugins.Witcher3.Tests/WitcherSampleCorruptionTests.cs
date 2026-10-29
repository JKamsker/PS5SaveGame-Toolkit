using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherSampleCorruptionTests
{
    [Fact]
    public void EverySampleRejectsTruncatedPhysicalChunkBoundaries()
    {
        foreach (var file in Samples())
        {
            var bytes = file.ReadAllBytes();
            var container = WitcherContainer.Parse(bytes);
            int[] headerCuts = [0, 1, 7, 8, 12, 15, 16, 20, 24, 27, container.HeaderSize - 1, container.HeaderSize];
            var cuts = headerCuts.Concat(container.Chunks.SelectMany(c => new[] { c.Offset + c.StoredSize / 2, c.Offset + c.StoredSize - 1 }));
            foreach (var cut in cuts.Distinct())
            {
                Assert.Throws<SaveFormatException>(() => RobustnessSave.OpenContainer(bytes[..cut]));
            }
            Assert.Equal(bytes, file.ReadAllBytes());
        }
    }

    [Fact]
    public void EverySampleRejectsCorruptDirectoriesAndRecognizedTokenLengths()
    {
        foreach (var file in Samples())
        {
            var bytes = file.ReadAllBytes();
            var container = WitcherContainer.Parse(bytes);
            var directory = Sav3Directory.Parse(container);
            var tree = Sav3Tree.Parse(container.Payload, directory);
            var original = container.Payload;
            var table = directory.VariableTableOffset;
            var nm = BinaryData.PayloadOffset(original, table - 10, container.HeaderSize);
            var rb = BinaryData.PayloadOffset(original, table - 6, container.HeaderSize);
            (int Offset, int Value)[] corruptions = [
                (original.Length - 6, int.MaxValue), (table - 10, -1), (table - 6, int.MaxValue),
                (table, 200001), (table + 4, container.HeaderSize + rb), (table + 8, 0),
                (nm + 6, int.MaxValue), (rb + 2, int.MaxValue), (rb + 8, int.MaxValue),
            ];
            foreach (var (offset, value) in corruptions)
            {
                var changed = original.ToArray();
                RobustnessSave.WriteInt(changed, offset, value);
                AssertRejectsPayload(container, changed);
            }
            foreach (var tag in new[] { "SS", "BLCK", "PORP", "AVAL" })
            {
                var node = tree.Nodes.First(n => n.Tag == tag);
                foreach (var length in new[] { -1, int.MaxValue })
                {
                    var changed = original.ToArray();
                    var lengthOffset = tag switch { "SS" => 2, "BLCK" => 6, _ => 8 };
                    RobustnessSave.WriteInt(changed, node.Offset + lengthOffset, length);
                    AssertRejectsPayload(container, changed);
                }
            }
            foreach (var tag in new[] { "BS", "VL", "PORP", "AVAL" })
            {
                var node = tree.Nodes.First(n => n.Tag == tag);
                var changed = original.ToArray();
                BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(node.Offset + (tag.Length == 2 ? 2 : 4)), 0);
                AssertRejectsPayload(container, changed);
            }
            var invalidAscii = original.ToArray();
            invalidAscii[nm + 15] = 0xFF;
            AssertRejectsPayload(container, invalidAscii);
            Assert.Equal(bytes, file.ReadAllBytes());
            using var document = new Witcher3Plugin().Open(file);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    private static void AssertRejectsPayload(WitcherContainer original, byte[] payload)
    {
        // Independently wrap as a raw chunk using the same virtual-offset origin. The production
        // writer deliberately reparses/rejects malformed payloads, so it cannot construct these inputs.
        var changed = RobustnessSave.StoredChunk(payload, payload.Length, original.HeaderSize);
        var error = Assert.Throws<SaveFormatException>(() => RobustnessSave.OpenContainer(changed));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private static SaveToolkit.Sdk.Saves.ISaveFile[] Samples() => EnabledWriteFixture.Files().ToArray();
}
