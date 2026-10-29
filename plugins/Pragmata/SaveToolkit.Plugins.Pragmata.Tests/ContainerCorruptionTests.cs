using System.Buffers.Binary;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class ContainerCorruptionTests
{
    [Fact]
    public void TruncatedPhysicalBlocksRejectWithRepairedOuterHashes()
    {
        var bytes = SyntheticDsss.Create(TypedPayloadFixture.Gameplay());
        var bodyEnd = bytes.Length - SyntheticDsss.TrailerSize;
        int[] cuts = [16, 17, 16 + DsssContainer.MetadataSize - 1, 16 + DsssContainer.MetadataSize,
            16 + DsssContainer.MetadataSize + 1, bodyEnd - 1];
        foreach (var cut in cuts)
        {
            var truncated = new byte[cut + SyntheticDsss.TrailerSize];
            bytes.AsSpan(0, cut).CopyTo(truncated);
            bytes.AsSpan(bodyEnd).CopyTo(truncated.AsSpan(cut));
            SampleEvidenceTests.RepairOuterHash(truncated);
            var error = Assert.Throws<SaveFormatException>(() => new DsssContainer(truncated));
            Assert.Contains("Truncated Mandarin block", error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public void ExtraPhysicalBytesRejectEvenWhenTheOuterHashMatches(int extraLength)
    {
        var bytes = SyntheticDsss.Create(TypedPayloadFixture.Gameplay());
        var expanded = new byte[bytes.Length + extraLength];
        var bodyEnd = bytes.Length - SyntheticDsss.TrailerSize;
        bytes.AsSpan(0, bodyEnd).CopyTo(expanded);
        bytes.AsSpan(bodyEnd).CopyTo(expanded.AsSpan(bodyEnd + extraLength));
        SampleEvidenceTests.RepairOuterHash(expanded);
        Assert.Contains("block layout", Assert.Throws<SaveFormatException>(() => new DsssContainer(expanded)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16, "Invalid Mandarin key/IV metadata")]
    [InlineData(16 + 64, "embedded key/IV metadata mismatch")]
    [InlineData(16 + DsssContainer.ChecksumOffset, "CityHash64")]
    [InlineData(16 + DsssContainer.MetadataSize, "CityHash64")]
    public void MetadataAndCiphertextDamageReachTheirOwnValidationLayers(int offset, string diagnostic)
    {
        var bytes = SyntheticDsss.Create(TypedPayloadFixture.Gameplay());
        bytes[offset] ^= 1;
        SampleEvidenceTests.RepairOuterHash(bytes);
        Assert.Contains(diagnostic, Assert.Throws<SaveFormatException>(() => new DsssContainer(bytes)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void ValidIntegrityCannotHideTruncatedTypedPayloads(int removed)
    {
        var plaintext = TypedPayloadFixture.Gameplay();
        var bytes = SyntheticDsss.Create(plaintext[..^removed]);
        // Independent envelope acceptance establishes that the rejection reaches typed parsing.
        Assert.Equal(plaintext[..^removed], new DsssContainer(bytes).Plaintext);
        var file = MemorySaveSet.SingleFile("data000.bin", bytes, "PPSA02530");
        var error = Assert.Throws<SaveFormatException>(() => new PragmataPlugin().Open(file));
        Assert.Contains("plaintext 0x", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllRealSamplesRejectLateBlockTruncationAndTypedCountDamageWithCorrectHashes()
    {
        var files = SampleEvidenceTests.Files().ToArray();
        Assert.Equal(12, files.Length);
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            var bodyEnd = original.Length - SyntheticDsss.TrailerSize;
            var truncated = new byte[original.Length - 1];
            original.AsSpan(0, bodyEnd - 1).CopyTo(truncated);
            original.AsSpan(bodyEnd).CopyTo(truncated.AsSpan(bodyEnd - 1));
            SampleEvidenceTests.RepairOuterHash(truncated);
            Assert.Contains("Truncated Mandarin block", Assert.Throws<SaveFormatException>(() =>
                new DsssContainer(truncated)).Message, StringComparison.Ordinal);

            var container = new DsssContainer(original);
            var plaintext = container.Plaintext.ToArray();
            var root = new TypedReader(plaintext).Parse()[0];
            BinaryPrimitives.WriteUInt32LittleEndian(plaintext.AsSpan(root.Offset), uint.MaxValue);
            var corrupted = container.Rebuild(plaintext);
            var input = MemorySaveSet.SingleFile(file.FileName, corrupted, "PPSA02530");
            Assert.Contains("element count", Assert.Throws<SaveFormatException>(() =>
                new PragmataPlugin().Open(input)).Message, StringComparison.Ordinal);
            Assert.Equal(original, file.ReadAllBytes());
        }
    }
}
