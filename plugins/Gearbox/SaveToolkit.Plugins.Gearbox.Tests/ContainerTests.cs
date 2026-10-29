using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ContainerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HeaderMetadataAndOpaqueTail_SurviveNoOpAndEdits(bool wonderlands, bool profile)
    {
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        byte[] tail = [0xAA, 0xBB, 0xCC];
        var bytes = GearboxTestData.CreateContainer(wonderlands, profile, true, tail);
        var container = GearboxContainer.Parse(bytes, title, profile);
        var root = container.Decode();
        Assert.Equal(bytes, container.Encode(root));
        if (profile) root.SetCategoryQuantity("bank_inventory_category_list", GearboxOverview.GoldenKeyHash, 5);
        else root.SetInteger("experience_points", 10_000);
        var output = container.Encode(root);
        Assert.True(bytes.AsSpan(0, container.PayloadOffset - 4).SequenceEqual(output.AsSpan(0, container.PayloadOffset - 4)));
        Assert.Equal(tail, output[^tail.Length..]);
        Assert.Equal(output.Length - container.PayloadOffset - tail.Length, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(container.PayloadOffset - 4)));
        var reopened = GearboxContainer.Parse(output, title, profile).Decode();
        Assert.Equal(profile ? 5 : 10_000, profile ? reopened.CategoryQuantity("bank_inventory_category_list", GearboxOverview.GoldenKeyHash) : reopened.Integer("experience_points"));
    }

    [Fact]
    public void HeaderLengthVersionTitleAndClassMismatches_AreRejected()
    {
        var bytes = GearboxTestData.CreateContainer(false, false);
        Assert.Throws<SaveFormatException>(() => GearboxContainer.Parse(bytes, "PPSA01494", false));
        Assert.Throws<SaveFormatException>(() => GearboxContainer.Parse(bytes, "PPSA01493", false));
        Assert.Throws<SaveFormatException>(() => GearboxContainer.Parse(bytes, "PPSA01463", true));
        foreach (var offset in new[] { 4, 12, 22 })
        {
            var damaged = bytes.ToArray();
            damaged[offset] = 0xFF;
            Assert.Throws<SaveFormatException>(() => GearboxContainer.Parse(damaged, "PPSA01463", false));
        }
    }

    [Fact]
    public void CloneRetainsNewRecordProvenance_AfterRejectedBatch()
    {
        var container = GearboxContainer.Parse(GearboxTestData.CreateContainer(false, true), "PPSA01463", true);
        using var document = new GearboxDocument("profile", "PPSA01463", container, container.Decode(), SaveToolkit.Sdk.References.IReferenceContext.Empty);
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new("keys/2420693941", FieldValue.Of(9L))]).Succeeded);
        Assert.False(document.ApplyEdits([new("keys/2420693941", FieldValue.Of(-1L))]).Succeeded);
        Assert.Equal(9, document.GetValue("keys/2420693941").AsInt64());
        Assert.True(document.ApplyEdits([new("keys/2420693941", FieldValue.Of(0L))]).Succeeded);
        Assert.Equal(container.Original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void InvalidUnicodeAndOversizedNames_AreRejectedWithoutMutation()
    {
        var container = GearboxContainer.Parse(GearboxTestData.CreateContainer(false, false), "PPSA01463", false);
        using var document = new GearboxDocument("1", "PPSA01463", container, container.Decode(), SaveToolkit.Sdk.References.IReferenceContext.Empty);
        _ = document.BuildView();
        foreach (var name in new[] { "\uD800", "bad\nname", new string('a', 100) })
        {
            Assert.False(document.ApplyEdits([new("character/name", FieldValue.Of(name))]).Succeeded);
            Assert.Equal(container.Original, document.Serialize().Single().Content.ToArray());
        }
    }

    [Fact]
    public void SingularMessageFragments_AreRejectedInsteadOfMisinterpreted() =>
        Assert.Throws<SaveFormatException>(() => ProtoMessage.Parse([0x22, 0, 0x22, 0], GearboxSchema.Bl3, "Character"));
}
