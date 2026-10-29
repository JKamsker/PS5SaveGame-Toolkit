using System.Buffers.Binary;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherDurabilityPreservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoringImportedZeroPreservesItsSignAndExactInput(bool negative)
    {
        var parsed = EnabledWriteFixture.Read(SyntheticPlayerSave.Create());
        var item = parsed.Player.Inventory!.Items[0];
        var originalBits = negative ? int.MinValue : 0;
        BinaryPrimitives.WriteInt32LittleEndian(parsed.Container.Payload.AsSpan(item.DurabilityOffset), originalBits);
        var input = SyntheticSave.Wrap(parsed.Container.Payload);
        var file = Assert.Single(SaveSetImporter.Import(
            [new ImportSource("PPSA04018/sdimg_TEST/Manual.sav", () => new MemoryStream(input))],
            Path.Combine(TestScratch.ProcessFolder, Guid.NewGuid().ToString("N"))).AllDiskFiles);
        var plugin = new Witcher3Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("zero-durability", file, plugin, document);
        _ = session.BuildView();
        _ = session.QueryCollection(InventoryViews.CollectionId, new QueryableCollectionQuery { Limit = 200 });
        // Either sign represents the imported numeric value; direct callers can preserve both signs.
        var oppositeZero = FieldValue.Of(negative ? 0.0 : -0.0);
        Assert.True(document.ApplyEdits([new ValueEdit(item.DurabilityId, oppositeZero)]).Succeeded);
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());

        Assert.True(session.ApplyEdits([EnabledWriteFixture.Incoming(item.DurabilityId, FieldValue.Of(25.0))]).Ok);
        var changed = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.True(session.ApplyEdits([EnabledWriteFixture.Incoming(item.DurabilityId, oppositeZero)]).Ok);
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(changed, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(originalBits, BitConverter.SingleToInt32Bits(
            EnabledWriteFixture.Read(Assert.Single(document.Serialize()).Content.ToArray()).Player.Inventory!.Items[0].Durability));
    }
}
