using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class DdlCorruptionTests
{
    [Theory]
    [InlineData("CareerBolts", 0x4A96B708u)]
    [InlineData("Playtime", 0x06DFCC00u)]
    [InlineData("Difficulty", 0x605D31D6u)]
    [InlineData("Checkpoint", RiftPlayerView.CampaignTag)]
    [InlineData("", 0xEDB88320u)]
    public void EngineNameHashMatchesPublishedSchemaAndSampleIdentifiers(string name, uint expected) =>
        Assert.Equal(expected, DdlReader.NameHash(System.Text.Encoding.UTF8.GetBytes(name)));

    [Theory]
    [InlineData("magic")]
    [InlineData("size")]
    [InlineData("field-count")]
    [InlineData("value-count")]
    [InlineData("name-bounds")]
    [InlineData("name-hash")]
    [InlineData("type")]
    [InlineData("nan")]
    [InlineData("bool")]
    [InlineData("terminator")]
    [InlineData("padding")]
    public void CorruptPlayerDdlIsRejected(string corruption)
    {
        var original = PlayerCorpusTests.Find("i29slot0-s-autosave", 0).ReadAllBytes();
        var container = Dat1Container.Parse(original);
        var section = container.Sections.Single(s => s.Tag == RiftPlayerView.CampaignTag);
        var fields = new DdlReader(container, section).Read();
        var start = checked((int)section.Offset);
        var bytes = original.ToArray();
        switch (corruption)
        {
            case "magic": bytes[start + 4] ^= 1; break;
            case "size": Write(start + 12, section.Size); break;
            case "field-count": Write(start + 8, uint.MaxValue); break;
            case "value-count": Write(start + 20, 0x0DFFFFF0); break;
            case "name-bounds": Write(start + 16 + fields.Count * 8, (uint)bytes.Length); break;
            case "name-hash": bytes[start + 16] ^= 1; break;
            case "type": Write(start + 20, 0xFE000010); break;
            case "nan": Write(fields["Playtime"].Offset, 0x7FC00000); break;
            case "bool": bytes[fields["IsChallengeMode"].Offset] = 2; break;
            case "terminator":
                var text = fields["Version"].Offset;
                var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(text));
                bytes[text + 16 + length] = 1;
                break;
            case "padding": bytes[checked((int)section.End) - 1] = 1; break;
        }
        Assert.Throws<SaveFormatException>(() => new InsomniacPlugin().Open(MemorySaveSet.SingleFile("i29-s.save", bytes)));
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }
}
