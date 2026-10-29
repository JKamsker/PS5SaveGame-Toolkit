using System.Buffers.Binary;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class FloatRepresentationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(int.MinValue)]
    public void ReturningToNumericZeroRestoresImportedFloatBits(int zeroBits)
    {
        var container = new DsssContainer(SyntheticInventory.Create(2));
        var plaintext = container.Plaintext.ToArray();
        var target = new SaveLayout(plaintext).Items[0].Find(SyntheticInventory.FloatHash)!;
        BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(target.Offset), zeroBits);
        var original = container.Rebuild(plaintext);
        using var document = new PragmataDocument("data000.bin", original);
        var field = document.Field(document.Layout.Items[0].Find(SyntheticInventory.FloatHash)!, raw: true);

        Assert.Empty(document.ApplyEdits([new(field.ValueId, FieldValue.Of(0.0))]).Changes);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1.0))]).Succeeded);
        var modified = document.Serialize().Single().Content.ToArray();
        var expected = plaintext.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(target.Offset), 1.0f);
        Assert.Equal(expected, new DsssContainer(modified).Plaintext);

        // A normal +0 assignment has the same semantic value as the imported zero.
        // It must restore the original sign, just like the initial numeric no-op.
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(0.0))]).Succeeded);
        var restored = document.Serialize().Single().Content.ToArray();
        Assert.Equal(zeroBits, BinaryPrimitives.ReadInt32LittleEndian(new DsssContainer(restored).Plaintext.AsSpan(target.Offset)));
        Assert.Equal(original, restored);
    }
}
