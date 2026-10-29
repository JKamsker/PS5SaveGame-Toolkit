using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartInventoryRangeTests
{
    [Theory]
    [InlineData("Amount", (ulong)long.MaxValue + 1)]
    [InlineData("Amount", ulong.MaxValue)]
    [InlineData("ItemId", (ulong)long.MaxValue + 1)]
    [InlineData("ItemId", ulong.MaxValue)]
    public void OversizedInventoryIntegersRejectWithAFieldSpecificFormatError(string field, ulong value)
    {
        var error = Assert.Throws<SaveFormatException>(() => WriteTestData.Open(
            InventoryFixture.Create(unsignedField: field, unsignedValue: value)));
        Assert.Equal($"Inventory {field} is outside the supported signed 64-bit range.", error.Message);
    }

    [Theory]
    [InlineData("Amount", 0UL)]
    [InlineData("Amount", (ulong)long.MaxValue)]
    [InlineData("ItemId", 0UL)]
    [InlineData("ItemId", (ulong)long.MaxValue)]
    public void RepresentableUnsignedInventoryValuesRemainAvailableAndByteIdentical(string field, ulong value)
    {
        var bytes = InventoryFixture.Create(unsignedField: field, unsignedValue: value);
        using var document = WriteTestData.Open(bytes);
        var index = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes));
        var state = new InventoryState(index, null);
        Assert.Equal(2, state.Items.Count);
        Assert.All(state.Items, item => Assert.Equal((long)value, field == "Amount" ? item.Amount : item.InstanceId));
        if (field == "Amount") Assert.All(state.Items, item => Assert.False(state.CanSetAmount(item)));
        _ = document.BuildView();
        _ = WriteTestData.Table(document);
        Assert.Equal(bytes, WriteTestData.Output(document));
    }
}
