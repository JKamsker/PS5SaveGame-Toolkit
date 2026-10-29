using System.Buffers.Binary;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using System.Security.Cryptography;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class SyntheticFixtureTests
{
    [Fact]
    public void SessionFileOwnsExactBytesAndCleansOnlyItsFolderOnNormalOrExceptionalExit()
    {
        var bytes = SyntheticInventory.Create(2);
        using var retained = new SyntheticSessionFile(bytes);
        string? disposedRoot = null;
        void FailWhileOwningFixture()
        {
            using var temporary = new SyntheticSessionFile(bytes);
            disposedRoot = temporary.StorageRoot;
            Assert.NotEqual(retained.StorageRoot, temporary.StorageRoot);
            Assert.Equal(bytes, temporary.File.ReadAllBytes());
            Assert.Equal(bytes.Length, temporary.File.Length);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), temporary.File.Sha256);
            using var reopened = new PragmataPlugin().Open(temporary.File);
            Assert.Equal(bytes, reopened.Serialize().Single().Content.ToArray());
            throw new InvalidOperationException("Exercise disposal during test failure.");
        }
        var error = Assert.Throws<InvalidOperationException>(FailWhileOwningFixture);
        Assert.Equal("Exercise disposal during test failure.", error.Message);
        Assert.NotNull(disposedRoot);
        Assert.False(Directory.Exists(disposedRoot));
        Assert.Equal(bytes, retained.File.ReadAllBytes());
        retained.Dispose();
        Assert.False(Directory.Exists(retained.StorageRoot));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(251)]
    [InlineData(1000)]
    public void InventoryIsAuthoredWithoutSampleFillerAndPreservesEveryRequestedRecord(int count)
    {
        var bytes = SyntheticInventory.Create(count);
        Assert.Equal(bytes, SyntheticInventory.Create(count));
        var container = new DsssContainer(bytes);
        var layout = new SaveLayout(container.Plaintext);
        Assert.Equal(3, layout.Roots.Count);
        Assert.Equal(count, layout.Items.Count);
        for (var index = 0; index < count; index++)
        {
            var item = layout.Items[index];
            Assert.Equal((uint)index + 1, BinaryPrimitives.ReadUInt32LittleEndian(
                container.Plaintext.AsSpan(item.Find(KnownHashes.ItemId)!.Offset)));
            Assert.Equal(index % 11, BinaryPrimitives.ReadInt32LittleEndian(
                container.Plaintext.AsSpan(item.Find(KnownHashes.Quantity)!.Offset)));
            Assert.Equal(index % 2 == 0 ? -2.5f : 4.75f, BinaryPrimitives.ReadSingleLittleEndian(
                container.Plaintext.AsSpan(item.Find(SyntheticInventory.FloatHash)!.Offset)));
        }
        var reader = new TypedReader(container.Plaintext);
        _ = reader.Parse();
        Assert.Equal(container.Plaintext.Length, reader.Position);
        using var document = new PragmataDocument("data000.bin", bytes);
        _ = document.BuildView();
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }
}
