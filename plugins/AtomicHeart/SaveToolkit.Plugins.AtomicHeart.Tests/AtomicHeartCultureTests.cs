using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartCultureTests
{
    [Theory]
    [InlineData("en-US", false)]
    [InlineData("ar-SA", false)]
    [InlineData("th-TH", true)]
    [InlineData("tr-TR", true)]
    public void SignedMapKeysAndArrayIndicesKeepInvariantFieldPaths(string cultureName, bool overrideSigns)
    {
        using var scope = Culture(cultureName, overrideSigns);
        var bytes = MapProperties();
        var reader = SaveExtensionBinary.Cursor(bytes);
        var properties = new PropertyReader(bytes).ReadSequence(ref reader);
        Assert.Equal("-123", Assert.Single(Assert.Single(properties).Children).Name);
        var index = new RecordIndex(bytes, properties, [], []);
        var views = new RecordViews(index, (id, label, spec, get) => new(id, label, spec, get()), p => p.Value!.Value);
        var field = Assert.IsType<FieldNode>(Assert.Single(views.Expand("inspect/group/2/0")));
        Assert.Equal("SlotData/Elements[-2]/-123[0]/Amount[-3]", field.ValueId);
    }

    [Theory]
    [InlineData("en-US", false)]
    [InlineData("ar-SA", false)]
    [InlineData("th-TH", true)]
    [InlineData("tr-TR", true)]
    public void EmptyItemSentinelKeepsItsInvariantRowKey(string cultureName, bool overrideSigns)
    {
        var bytes = InventoryFixture.Create();
        var index = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes));
        var original = new InventoryState(index, null).Items[0];
        var instance = original.Element.Children.Single(p => p.Name == "ItemId");
        BinaryPrimitives.WriteInt64LittleEndian(index.Plain.AsSpan(instance.Payload.Offset), -1);
        bytes = SlotFixture.Create(plain: index.Plain);
        using var scope = Culture(cultureName, overrideSigns);
        index = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes));
        var state = new InventoryState(index, null);
        var key = original.Key[..original.Key.LastIndexOf('/')] + "/-1";
        Assert.Equal(key, state.Items[0].Key);
        Assert.True(state.TryGetOrdinal(key, out var ordinal));
        Assert.Equal(0, ordinal);
        using var document = SlotFixture.Open(bytes);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static TestCulture Culture(string name, bool overrideSigns)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(name).Clone();
        // Regional overrides are independent of language; exercise customized Thai/Turkish signs.
        if (overrideSigns) culture.NumberFormat.NegativeSign = "~";
        return new TestCulture(culture);
    }

    private static byte[] MapProperties()
    {
        using var payload = new MemoryStream();
        using var values = new BinaryWriter(payload);
        values.Write(0); // No removed keys.
        values.Write(1);
        values.Write(-123L);
        SlotFixture.String(values, "Amount");
        SlotFixture.String(values, "IntProperty");
        values.Write(sizeof(int));
        values.Write(-3); // Serialized array index.
        values.Write((byte)0); // No property GUID.
        values.Write(7);
        PlainFixture.End(values);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        SlotFixture.String(writer, "Elements");
        SlotFixture.String(writer, "MapProperty");
        writer.Write(checked((int)payload.Length));
        writer.Write(-2); // Serialized array index.
        SlotFixture.String(writer, "Int64Property");
        SlotFixture.String(writer, "StructProperty");
        writer.Write((byte)0);
        writer.Write(payload.ToArray());
        PlainFixture.End(writer);
        return stream.ToArray();
    }
}
