using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class SampleCorruptionTests
{
    [Theory]
    [InlineData("chunk overlap")]
    [InlineData("node offset")]
    [InlineData("node count")]
    [InlineData("payload ID")]
    [InlineData("inventory count")]
    [InlineData("item count")]
    [InlineData("facts count")]
    [InlineData("package length")]
    public void InMemoryCorruptionsOfEverySampleAreRejectedWithFormatErrors(string corruption)
    {
        foreach (var file in ProgressionSampleTests.Files())
        {
            var originalBytes = file.ReadAllBytes();
            var container = CsavContainer.Parse(originalBytes);
            var tree = CsavTree.Parse(container);
            var bytes = originalBytes.ToArray();
            var inventory = Assert.Single(tree.Nodes, n => n.Name == "inventory");
            var payload = container.Payload.ToArray();
            switch (corruption)
            {
                case "chunk overlap":
                    CsavCorruptionFixture.SetWord(bytes, CsavCorruptionFixture.TableOffset + CsavCorruptionFixture.EntrySize,
                        container.Chunks[0].Offset);
                    break;
                case "node offset":
                    var position = container.DirectoryOffset + 4;
                    _ = BinaryData.PackedInt(bytes, ref position);
                    _ = BinaryData.PackedName(bytes, ref position);
                    CsavCorruptionFixture.SetWord(bytes, position + 8, int.MinValue);
                    break;
                case "node count":
                    bytes[container.DirectoryOffset + 4] = 0x81; // Negative packed count.
                    break;
                default:
                    switch (corruption)
                    {
                        case "payload ID": CsavCorruptionFixture.SetWord(payload, tree.Nodes[0].Offset, -1); break;
                        case "inventory count": CsavCorruptionFixture.SetWord(payload, inventory.Offset + 4, int.MaxValue); break;
                        case "item count": CsavCorruptionFixture.SetWord(payload, inventory.Offset + 16, int.MaxValue); break;
                        case "facts count":
                            payload[tree.Nodes.First(n => n.Name == "FactsTable").Offset + 4] = 0x81;
                            break;
                        case "package length":
                            CsavCorruptionFixture.SetWord(payload,
                                Assert.Single(tree.Nodes, n => n.Name == "ScriptableSystemsContainer").Offset + 4, int.MaxValue);
                            break;
                        default: throw new ArgumentException("Unknown corruption case.", nameof(corruption));
                    }
                    // Independently frame mutated sample payloads: the production writer rightly rejects corrupt output.
                    var nodes = tree.Nodes.Select(n => new FixtureNode(n.Name, n.NextId, n.ChildId, n.Offset, n.Size)).ToArray();
                    bytes = CsavCorruptionFixture.Create(payload, nodes, container.Chunks.Select(c => c.ExpandedSize).ToArray(), compressed: true);
                    CsavCorruptionFixture.SetWord(bytes, 4, container.SaveVersion);
                    CsavCorruptionFixture.SetWord(bytes, 8, container.GameVersion);
                    CsavCorruptionFixture.SetWord(bytes, 21, container.ArchiveVersion);
                    break;
            }
            var error = Assert.Throws<SaveFormatException>(() => ProgressionSampleTests.Open(bytes));
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            // Mutation helpers must never modify the original input or the private sample on disk.
            Assert.Equal(originalBytes, file.ReadAllBytes());
            Assert.Equal(originalBytes, container.Original);
        }
    }
}
