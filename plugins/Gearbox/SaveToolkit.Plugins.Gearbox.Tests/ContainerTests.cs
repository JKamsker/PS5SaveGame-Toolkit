using System.Buffers.Binary;
using System.Text;
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
        var bytes = Create(wonderlands, profile, true, tail);
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
        var bytes = Create(false, false);
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
        var container = GearboxContainer.Parse(Create(false, true), "PPSA01463", true);
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
        var container = GearboxContainer.Parse(Create(false, false), "PPSA01463", false);
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

    internal static byte[] Create(bool wonderlands, bool profile, bool unicode = false, byte[]? tail = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("GVAS"u8);
        writer.Write(2);
        writer.Write(516);
        writer.Write((ushort)4);
        writer.Write((ushort)20);
        writer.Write((ushort)3);
        writer.Write(1234);
        WriteString(writer, wonderlands ? "DAFFODIL-test" : "OAK-test", unicode);
        writer.Write(3);
        writer.Write(1);
        writer.Write(Enumerable.Range(0, 16).Select(n => (byte)n).ToArray());
        writer.Write(99);
        WriteString(writer, (wonderlands, profile) switch
        {
            (false, false) => "OakSaveGame", (false, true) => "BP_DefaultOakProfile_C",
            (true, false) => "BPSaveGame_Default_C", (true, true) => "OakProfile",
        }, unicode);
        byte[] plain = profile ? [0x08, 0x01] : [0x22, 0x06, 0x0A, 0x04, 0x74, 0x65, 0x73, 0x74, 0x38, 0x00];
        var ciphertext = BogoCrypt.Encode(plain, profile);
        writer.Write(ciphertext.Length);
        writer.Write(ciphertext);
        writer.Write(tail ?? []);
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string text, bool unicode)
    {
        writer.Write((text.Length + 1) * (unicode ? -1 : 1));
        writer.Write((unicode ? Encoding.Unicode : Encoding.UTF8).GetBytes(text + '\0'));
    }
}
