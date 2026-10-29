using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseCorruptionTests
{
    [Theory]
    [InlineData(AriseFormat.PayloadOffset)]
    [InlineData(AriseFormat.PayloadOffset + 4)]
    [InlineData(AriseFormat.HashOffset)]
    [InlineData(AriseFormat.ModeOffset)]
    [InlineData(AriseFormat.ModeOffset + 1)]
    [InlineData(AriseFormat.CipherOffset)]
    [InlineData(AriseFormat.FileSize - 1)]
    public void CorruptHeaderHashOrCiphertext_IsRejected(int offset)
    {
        foreach (var file in AriseSampleTests.GameplayFiles())
        {
            var bytes = file.ReadAllBytes();
            bytes[offset] ^= 0x80;
            Assert.Throws<SaveFormatException>(() => new TalesOfArisePlugin().Open(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA02745")));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(4096)]
    [InlineData(AriseFormat.FileSize - 1)]
    [InlineData(AriseFormat.FileSize + 1)]
    public void WrongSize_IsRejectedEvenWithoutSamples(int size) =>
        Assert.Throws<SaveFormatException>(() => AriseFormat.Decode(new byte[size]));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TruncatedEncryptedPrefixes_RejectAtPublicBoundaryBeforeDecryption(bool reversePhysicalOrder)
    {
        var bytes = new AriseTestContainer(reversePhysicalOrder).Encrypt();
        AssertTruncatedPrefixesRejected(bytes);
    }

    [Fact]
    public void EverySampleAndBackup_RejectsTruncatedEncryptedPrefixes()
    {
        foreach (var file in AriseSampleTests.GameplayFiles())
        {
            AssertTruncatedPrefixesRejected(file.ReadAllBytes());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(64)]
    public void DeletedCiphertextBytes_WithRepairedSha1_StillRejectTheContainerSize(int count)
    {
        var original = new AriseTestContainer().Encrypt();
        // Delete inside the ciphertext, rather than merely shortening the end of the file.
        var cut = AriseFormat.CipherOffset + 0x40;
        var bytes = new byte[original.Length - count];
        original.AsSpan(0, cut).CopyTo(bytes);
        original.AsSpan(cut + count).CopyTo(bytes.AsSpan(cut));
        AriseSampleTests.Hash(bytes.AsSpan(AriseFormat.CipherOffset)).CopyTo(bytes, AriseFormat.HashOffset);
        AssertWrongSizeRejected(bytes);
    }

    [Theory]
    [InlineData("unaligned-directory")]
    [InlineData("directory-in-header")]
    [InlineData("directory-overflow")]
    [InlineData("directory-outside")]
    [InlineData("directory-marker")]
    [InlineData("section-count")]
    [InlineData("overlap")]
    [InlineData("cycle")]
    [InlineData("section-id")]
    [InlineData("section-size")]
    [InlineData("section-version")]
    public void MalformedEncryptedStructure_WithValidSha1_IsRejected(string mutation)
    {
        var original = AriseSampleTests.GameplayFiles().First().ReadAllBytes();
        var save = AriseFormat.Decode(original);
        var plain = save.Payload;
        var directory = checked((int)AriseFormat.U32(plain, 0x38));
        var header = save.Sections[0].Offset - AriseFormat.HeaderSize;
        var (offset, value) = mutation switch
        {
            "unaligned-directory" => (0x38, 0x61u),
            "directory-in-header" => (0x38, 0x10u),
            "directory-overflow" => (0x38, uint.MaxValue),
            "directory-outside" => (0x38, (uint)plain.Length),
            "directory-marker" => (directory, 99u),
            "section-count" => (directory + 4, uint.MaxValue),
            "overlap" => (directory + 8, 0x20u),
            "cycle" => (header + 12, (uint)header),
            "section-id" => (header + 4, 0u),
            "section-size" => (header + 8, uint.MaxValue),
            "section-version" => (header, uint.MaxValue),
            _ => throw new ArgumentException("Unknown mutation.", nameof(mutation)),
        };
        BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(offset), value);
        // Encrypt only the prefix needed to reach the malformed structure, then repair SHA1.
        // This bypasses the production writer's own structural validation on purpose.
        using var cipher = new AriseCipher();
        Encrypt(0x20, 0x40);
        Encrypt(directory, 0x40);
        Encrypt(header, AriseFormat.HeaderSize);
        AriseSampleTests.Hash(original.AsSpan(AriseFormat.CipherOffset)).CopyTo(original, AriseFormat.HashOffset);
        var exception = Assert.Throws<SaveFormatException>(() => AriseFormat.Decode(original));
        Assert.DoesNotContain("SHA1 mismatch", exception.Message, StringComparison.Ordinal);

        void Encrypt(int start, int length) => cipher.Transform(plain.AsSpan(start, length), original.AsSpan(AriseFormat.PayloadOffset + start, length), true);
    }

    [Fact]
    public void SystemSave_IsNotClaimedOrDecrypted()
    {
        var file = ((SaveToolkit.Sdk.Saves.ISaveSet)SampleSaves.Load("PPSA02745")).AllFiles.First(f => f.FileName == "SystemSaveData.sav");
        var plugin = new TalesOfArisePlugin();
        Assert.False(plugin.Probe(file).IsMatch);
        Assert.Throws<SaveFormatException>(() => plugin.Open(file));
    }

    private static void AssertTruncatedPrefixesRejected(byte[] original)
    {
        var save = AriseFormat.Decode(original);
        var directory = checked((int)AriseFormat.U32(save.Payload, AriseTestContainer.DirectoryPointerOffset));
        var boundaries = new[]
        {
            0, AriseFormat.PayloadOffset, AriseFormat.PayloadOffset + 4, AriseFormat.HashOffset,
            AriseFormat.HashOffset + 20, AriseFormat.ModeOffset, AriseFormat.CipherOffset,
            AriseFormat.CipherOffset + 16, AriseFormat.CipherOffset + 0x40,
            AriseFormat.PayloadOffset + directory, AriseFormat.PayloadOffset + directory + 0x40,
            original.Length,
        }.Concat(save.Sections.SelectMany(section => new[]
        {
            AriseFormat.PayloadOffset + section.Offset - AriseFormat.HeaderSize,
            AriseFormat.PayloadOffset + section.Offset,
            AriseFormat.PayloadOffset + section.Offset + section.Size,
        }));
        var cuts = boundaries.SelectMany(boundary => new[] { boundary - 1, boundary, boundary + 1 })
            .Concat(Enumerable.Range(original.Length - 16, 16))
            .Where(length => length >= 0 && length < original.Length).Distinct().Order();
        foreach (var length in cuts)
        {
            AssertWrongSizeRejected(original.AsSpan(0, length).ToArray());
        }
    }

    private static void AssertWrongSizeRejected(byte[] bytes)
    {
        var plugin = new TalesOfArisePlugin();
        var file = MemorySaveSet.SingleFile(AriseTestContainer.FileName, bytes, AriseTestContainer.TitleId);
        Assert.False(plugin.Probe(file).IsMatch);
        var exception = Assert.Throws<SaveFormatException>(() => plugin.Open(file));
        Assert.Contains("size, magic or PS5 encryption mode", exception.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, file.ReadAllBytes());
    }
}
