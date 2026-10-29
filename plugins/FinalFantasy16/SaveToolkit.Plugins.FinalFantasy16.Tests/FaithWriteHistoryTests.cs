using System.Buffers.Binary;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithWriteHistoryTests
{
    private const string IdentityId = "inventory/1/item";
    private const string QuantityId = "inventory/1/quantity";
    private const string UpgradeQuantityId = "capacity/506/main/1";
    private const int SecondRecordOffset = 16;
    private const int OriginalQuantity = 567;
    private const int FirstQuantity = 100;
    private const int SecondQuantity = 200;
    private readonly FinalFantasy16Plugin _plugin = new();

    [Theory]
    [InlineData(506, 5)]
    [InlineData(5, 506)]
    public void MixedIdentityAndQuantityHistoryRestoresAppearingAndDisappearingUpgradeAliases(int originalId, int replacementId)
    {
        var original = Save(originalId);
        using var fixture = new FaithSessionFile(original);
        using var document = _plugin.Open(fixture.File);
        using var session = new DocumentSession("identity-quantity-history", fixture.File, _plugin, document);
        _ = session.BuildView();

        AssertAccepted(session.ApplyEdits([FaithWriteTests.Incoming(QuantityId, FieldValue.Of(FirstQuantity))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken));
        var first = Assert.Single(document.Serialize()).Content.ToArray();
        AssertAlias(document, originalId, FirstQuantity);

        AssertAccepted(session.ApplyEdits([
            FaithWriteTests.Incoming(IdentityId, FieldValue.Of(FaithCatalog.Key(replacementId))),
            FaithWriteTests.Incoming(QuantityId, FieldValue.Of(SecondQuantity)),
        ], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken));
        var second = Assert.Single(document.Serialize()).Content.ToArray();
        var itemOffset = FaithContainer.HeaderSize + FaithBlock.HeaderSize + SecondRecordOffset;
        FaithWriteTests.AssertOnlyRanges(original, second, [(itemOffset, 2 * sizeof(int))]);
        AssertAlias(document, replacementId, SecondQuantity);
        using (var reopened = _plugin.Open(FaithTestData.File(second)))
        {
            _ = reopened.BuildView();
            Assert.Equal(FaithCatalog.Key(replacementId), reopened.GetValue(IdentityId).AsString());
            Assert.Equal(SecondQuantity, reopened.GetValue(QuantityId).AsInt32());
            AssertAlias(reopened, replacementId, SecondQuantity);
        }

        Assert.True(session.Undo().Ok);
        Assert.Equal(first, Assert.Single(document.Serialize()).Content.ToArray());
        AssertAlias(document, originalId, FirstQuantity);
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);

        Assert.True(session.Redo().Ok);
        Assert.Equal(first, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(second, Assert.Single(document.Serialize()).Content.ToArray());
        AssertAlias(document, replacementId, SecondQuantity);
        Assert.False(session.State.CanRedo);
        Assert.Equal(original, fixture.File.ReadAllBytes());
    }

    [Fact]
    public void DuplicateIdentityRejectionPreservesEarlierHistoryAndRedo()
    {
        var original = Save(506);
        using var fixture = new FaithSessionFile(original);
        using var document = _plugin.Open(fixture.File);
        using var session = new DocumentSession("rejected-identity-history", fixture.File, _plugin, document);
        _ = session.BuildView();
        AssertAccepted(session.ApplyEdits([FaithWriteTests.Incoming(QuantityId, FieldValue.Of(FirstQuantity))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken));
        var accepted = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.True(session.Undo().Ok);
        var state = session.State;

        var rejected = session.ApplyEdits([
            FaithWriteTests.Incoming(QuantityId, FieldValue.Of(SecondQuantity)),
            FaithWriteTests.Incoming(IdentityId, FieldValue.Of("1")),
        ], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(rejected.Ok);
        Assert.Contains(rejected.Errors, error => error.Message.Contains("duplicate", StringComparison.Ordinal));
        Assert.Empty(rejected.Changes);
        Assert.Equal(state, session.State);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        AssertAlias(document, 506, OriginalQuantity);
        Assert.True(session.Redo().Ok);
        Assert.Equal(accepted, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.IsDirty);
    }

    private static void AssertAccepted(SessionEditResult result)
    {
        Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.True(result.StructureChanged);
        Assert.Contains(result.Warnings, warning => warning.Contains("Not validated on console", StringComparison.Ordinal));
    }

    private static void AssertAlias(ISaveDocument document, int itemId, int quantity)
    {
        var fields = FaithViewFields.All(document.BuildView());
        if (itemId != 506)
        {
            Assert.DoesNotContain(fields, field => field.ValueId == UpgradeQuantityId);
            return;
        }
        var alias = Assert.Single(fields, field => field.ValueId == UpgradeQuantityId);
        Assert.True(alias.Spec.ReadOnly);
        Assert.Equal(quantity, alias.Value.AsInt32());
        Assert.Equal(quantity, document.GetValue(UpgradeQuantityId).AsInt32());
    }

    private static byte[] Save(int itemId)
    {
        var payload = FaithTestData.Items();
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(SecondRecordOffset), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(SecondRecordOffset + sizeof(int)), OriginalQuantity);
        // Unknown flags, record padding, another block and trailer must survive every history step.
        payload[SecondRecordOffset + 2 * sizeof(int)] = 0x81;
        payload[SecondRecordOffset + 3 * sizeof(int)] = 0xA5;
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload),
            (99, 1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        bytes = [.. bytes, 0x12, 0x34];
        FaithTestData.FixCrc(bytes);
        return bytes;
    }
}
