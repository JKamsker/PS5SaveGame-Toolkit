using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class EditingTests
{
    private const uint TargetItem = 2227368435;

    [Fact]
    public void QuantityEditsInEveryGameplaySnapshotReopenAndReverseIdentically()
    {
        // Whole-output golden hashes were produced by the supplied Python toolkit, incrementing
        // only item 2227368435 by one in each original sample. No extracted save bytes are embedded.
        using var resource = typeof(EditingTests).Assembly.GetManifestResourceStream("Pragmata.EditedQuantityHashes.json")!;
        var hashes = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(resource)!;
        foreach (var file in SampleEvidenceTests.Files().Where(f => f.FileName != "data00-1.bin"))
        {
            var original = file.ReadAllBytes();
            using var document = new PragmataDocument(file.RelativePath, original);
            _ = document.BuildView();
            var quantity = Quantity(document);
            var old = document.GetValue(quantity.Path);
            if (file.FileName == "data000.bin")
            {
                Assert.Equal(2575, old.AsInt32());
                Assert.Equal(0x0a9bcc, quantity.Offset);
                Assert.Equal("7c416083/7c018857[6]/3185b7a5[5]/d18b252d", quantity.Path);
            }
            var outcome = document.ApplyEdits([new(quantity.Path, FieldValue.Of(old.AsInteger() + 1))]);
            Assert.True(outcome.Succeeded);
            var modified = document.Serialize().Single().Content.ToArray();
            Assert.Equal(hashes[file.FileName], Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(modified)));
            using var reopened = new PragmataDocument(file.RelativePath, modified);
            _ = reopened.BuildView();
            Assert.Equal(old.AsInteger() + 1, reopened.GetValue(Quantity(reopened).Path).AsInteger());
            AssertOnlyQuantityAndChecksumsChanged(original, modified, quantity.Offset);
            var undo = reopened.ApplyEdits(outcome.Changes.Reverse().Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray());
            Assert.True(undo.Succeeded);
            Assert.Equal(original, reopened.Serialize().Single().Content.ToArray());
        }
    }

    [Fact]
    public void BulkActionIsUndoableAndPreservesItemIdsAndEquippedId()
    {
        using var document = Gameplay();
        var original = document.Serialize().Single().Content.ToArray();
        var ids = document.Layout.Items.Select(item => document.Read(item.Find(KnownHashes.ItemId)!)).ToArray();
        var equipped = document.Read(document.Layout.Inventory!.Find(KnownHashes.EquippedItem)!);
        var outcome = document.InvokeAction(PragmataViews.QuantityAction, new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(17L) });
        Assert.True(outcome.Undoable);
        Assert.NotEmpty(outcome.Warnings);
        using var reopened = new PragmataDocument("data000.bin", document.Serialize().Single().Content.Span);
        Assert.All(reopened.Layout.Items, item => Assert.Equal(17, reopened.Read(item.Find(KnownHashes.Quantity)!).AsInt32()));
        Assert.Equal(ids, reopened.Layout.Items.Select(item => reopened.Read(item.Find(KnownHashes.ItemId)!)));
        Assert.Equal(equipped, reopened.Read(reopened.Layout.Inventory!.Find(KnownHashes.EquippedItem)!));
        Assert.True(document.ApplyEdits(outcome.Changes.Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray()).Succeeded);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(2147483648L)]
    public void InvalidBulkQuantityCannotPartiallyChangeItems(long quantity)
    {
        using var document = Gameplay();
        var before = document.Serialize().Single().Content.ToArray();
        Assert.Throws<ArgumentException>(() => document.InvokeAction(PragmataViews.QuantityAction,
            new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(quantity) }));
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void InvalidBatchesAreAtomicIncludingNarrowIntegersAndUnknownIds()
    {
        using var document = Gameplay();
        var quantity = Quantity(document);
        var before = document.Serialize().Single().Content.ToArray();
        foreach (var invalid in new[] { FieldValue.Of((Int128)int.MaxValue + 1), FieldValue.Of(true), FieldValue.Of("12") })
        {
            var outcome = document.ApplyEdits([
                new(document.Layout.Header!.Find(KnownHashes.Cleared)!.Path, FieldValue.Of(true)),
                new(quantity.Path, invalid),
            ]);
            Assert.False(outcome.Succeeded);
        }
        Assert.False(document.ApplyEdits([new(quantity.Path, FieldValue.Of(5L)), new("missing", FieldValue.Of(1L))]).Succeeded);
        Assert.False(document.ApplyEdits([new(quantity.Path, FieldValue.Of(5L)), new(quantity.Path, FieldValue.Of(6L))]).Succeeded);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void BulkEditsOfUnexposedQuantitiesDoNotPromiseFrameworkUndo()
    {
        var file = SampleEvidenceTests.Files().Single(f => f.FileName == "data000.bin");
        using var document = new PragmataDocument(file.RelativePath, file.ReadAllBytes());
        foreach (var quantity in new[] { 1L, 2L })
        {
            var outcome = document.InvokeAction(PragmataViews.QuantityAction,
                new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(quantity) });
            Assert.False(outcome.Undoable);
        }
        _ = document.BuildView();
        Assert.True(document.InvokeAction(PragmataViews.QuantityAction,
            new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(3L) }).Undoable);
    }

    internal static PragmataDocument Gameplay()
    {
        var file = SampleEvidenceTests.Files().Single(f => f.FileName == "data000.bin");
        var document = new PragmataDocument(file.RelativePath, file.ReadAllBytes());
        _ = document.BuildView();
        return document;
    }

    internal static SaveNode Quantity(PragmataDocument document) => document.Layout.Items
        .Single(item => document.Read(item.Find(KnownHashes.ItemId)!).AsUInt32() == TargetItem).Find(KnownHashes.Quantity)!;

    private static void AssertOnlyQuantityAndChecksumsChanged(byte[] original, byte[] modified, int plainOffset)
    {
        var save = new DsssContainer(original);
        var expectedPlaintext = save.Plaintext.ToArray();
        var originalValue = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(expectedPlaintext.AsSpan(plainOffset));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(expectedPlaintext.AsSpan(plainOffset), originalValue + 1);
        var reopened = new DsssContainer(modified);
        Assert.Equal(expectedPlaintext, reopened.Plaintext);
        // Full deterministic delta reconstruction also covers ciphertext padding, ElGamal pairs,
        // unknown metadata, header padding, and the preserved RSA trailer.
        Assert.Equal(save.Rebuild(expectedPlaintext), modified);
        Assert.Equal(original.AsSpan(original.Length - 140, 136).ToArray(), modified.AsSpan(modified.Length - 140, 136).ToArray());
    }
}
