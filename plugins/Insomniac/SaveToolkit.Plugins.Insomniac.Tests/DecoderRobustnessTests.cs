using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class DecoderRobustnessTests
{
    [Fact]
    public void CampaignWithoutOptionalGoldSectionDoesNotThrowAKeyLookupError()
    {
        var fixture = new DdlTestFixture(new DdlTestFixture.Field("CareerBolts", DdlValueKind.UInt32, [1, 0, 0, 0]));
        fixture.Write(16, RiftPlayerView.CampaignTag);
        var sink = new RobustnessTestSink();
        new Insomniac.RiftEvidenceDecoder().Decode(File(fixture.Bytes), sink);
        Assert.Equal(2, sink.AddCalls);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(64, false)]
    [InlineData(64, true)]
    public void CorruptInventoryRejectsBeforePreferencesCanReturnOrSinkCanStop(int depth, bool acceptsValues)
    {
        var sink = new RobustnessTestSink { MaxDepth = depth, AcceptsValues = acceptsValues };
        var bytes = WithInventory();
        var error = Assert.Throws<SaveFormatException>(() => new Insomniac.RiftEvidenceDecoder().Decode(File(bytes), sink));
        Assert.Contains("Inventory section", error.Message);
        Assert.Equal(0, sink.AddCalls);
    }

    [Fact]
    public void ShallowDepthDoesNotHideMalformedNestedDdl()
    {
        var fixture = new DdlTestFixture(new DdlTestFixture.Field("Child", DdlValueKind.Object, new byte[16]));
        var sink = new RobustnessTestSink { MaxDepth = 0 };
        Assert.Contains("Invalid object header", Assert.Throws<SaveFormatException>(() =>
            new Insomniac.RiftEvidenceDecoder().Decode(File(fixture.Bytes), sink)).Message);
        Assert.Equal(0, sink.AddCalls);
        Assert.Equal(0, sink.Truncations);
    }

    [Fact]
    public void ShallowDepthStillMarksValidNestedDdlTruncated()
    {
        var child = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(child.AsSpan(4), 0x03150044);
        var fixture = new DdlTestFixture(new DdlTestFixture.Field("Child", DdlValueKind.Object, child));
        var sink = new RobustnessTestSink { MaxDepth = 0 };
        new Insomniac.RiftEvidenceDecoder().Decode(File(fixture.Bytes), sink);
        Assert.Equal(0, sink.AddCalls);
        Assert.Equal(1, sink.Truncations);
    }

    // Two independently framed sections: a valid preference bool and an invalid inventory magic.
    private static byte[] WithInventory()
    {
        const int root = 48;
        const int inventory = 80;
        var bytes = new byte[96];
        Write(0, 0x44415431); Write(4, 8); Write(8, (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 2);
        Write(16, DdlSections.PreferencesTag); Write(20, root); Write(24, inventory - root);
        Write(28, InventoryReader.SectionTag); Write(32, inventory); Write(36, 16);
        "Flag\0"u8.CopyTo(bytes.AsSpan(40));
        Write(root + 4, 0x03150044); Write(root + 8, 1); Write(root + 12, 16);
        Write(root + 16, DdlReader.NameHash("Flag"u8)); Write(root + 20, 0x0F000010); Write(root + 24, 40);
        bytes[root + 28] = 1;
        Write(inventory, 0x12345678); Write(inventory + 4, 8);
        return bytes;
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }

    private static ISaveFile File(byte[] bytes) => MemorySaveSet.SingleFile("i29-s.save", bytes, "PPSA01474");

}
