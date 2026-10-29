using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class ArrayRobustnessTests
{
    [Theory]
    [InlineData(int.MaxValue, 4)]
    [InlineData(FieldTags.Class, 4)]
    [InlineData(FieldTags.Array, 4)]
    [InlineData(FieldTags.UInt32, 0)]
    [InlineData(FieldTags.UInt32, 8)]
    [InlineData(FieldTags.Struct, 0)]
    public void EmptyValueArraysRejectUnsupportedElementDefinitions(int type, int size)
    {
        using var payload = new TypedPayloadFixture();
        payload.Root(1, 1);
        payload.Array(2, type, size, 0, 0);
        var error = Assert.Throws<SaveFormatException>(() => new TypedReader(payload.Bytes).Parse());
        Assert.Contains("Unsupported", error.Message, StringComparison.Ordinal);
        Assert.Contains("plaintext 0x", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FieldTags.Enum, 1)]
    [InlineData(FieldTags.UInt32, 4)]
    [InlineData(FieldTags.Float64, 8)]
    [InlineData(FieldTags.Struct, 24)]
    [InlineData(FieldTags.String, 0)]
    public void EmptySupportedValueArraysStillConsumeTheirExactPayload(int type, int size)
    {
        using var payload = new TypedPayloadFixture();
        payload.Root(1, 1);
        payload.Array(2, type, size, 0, 0);
        var reader = new TypedReader(payload.Bytes);
        var root = Assert.Single(reader.Parse());
        Assert.Empty(Assert.Single(root.Children).Children);
        Assert.Equal(payload.Bytes.Length, reader.Position);
    }
}
