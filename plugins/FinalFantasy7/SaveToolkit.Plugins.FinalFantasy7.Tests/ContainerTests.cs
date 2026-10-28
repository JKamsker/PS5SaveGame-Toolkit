using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class ContainerTests
{
    [Theory]
    [InlineData("PPSA01911", "ff7remakecommon")]
    [InlineData("PPSA08664", "ff7rebirthcommon")]
    [InlineData("PPSA18936", "ff7rebirthdemocommon")]
    public void SyntheticCommon_ValidatesTailVariantsAndPreservesUnknownBytes(string title, string name)
    {
        var bytes = Common(title);
        bytes[0x100] = 0xA5;
        var file = MemorySaveSet.SingleFile(name, bytes, title);
        var plugin = new FinalFantasy7Plugin();
        Assert.True(plugin.Probe(file).IsMatch);
        using var document = plugin.Open(file);
        Assert.False(document.CanEdit);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(bytes, document.ReadBlob("file", 0, bytes.Length).ToArray());
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void Common_RejectsCorruptFraming(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        (int Offset, uint Value)[] corruptions =
        [
            (0, 0), (4, 0), (4, 16), (4, uint.MaxValue), (4, 0x420),
            (16, 0), (20, 0), (32, 99), (36, 0x401),
            (0x410, 0x00010000), (0x414, 1234), (0x418, 1), (0x41C, 99),
        ];
        foreach (var (offset, value) in corruptions)
        {
            var bytes = Common(title);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
            Assert.Throws<SaveFormatException>(() => LinkedRecords.Parse(bytes, schema, common: true));
        }

        Assert.Throws<SaveFormatException>(() => LinkedRecords.Parse(Common(title).AsSpan(0, 1055), schema, common: true));
        Assert.Throws<SaveFormatException>(() => LinkedRecords.Parse([.. Common(title), 0], schema, common: true));
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void Probe_RequiresTitleFilenameSizeAndHeader(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var plugin = new FinalFantasy7Plugin();
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile(schema.CommonName, Common(title), "PPSA99999")).IsMatch);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("unrelated", Common(title), title)).IsMatch);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile(schema.CommonName, Common(title)[..^1], title)).IsMatch);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile(schema.CommonName, new byte[1056], title)).IsMatch);
        Assert.True(plugin.Probe(MemorySaveSet.SingleFile(schema.CommonName + ".sav", Common(title), title)).IsMatch);
        foreach (var shortBytes in new[] { Array.Empty<byte>(), new byte[1], new byte[16], new byte[39] })
        {
            Assert.False(schema.MatchesFirstHeader(shortBytes, common: true));
        }

        Assert.False(schema.TryGetRole(schema.Prefix + "../", out _));
        Assert.False(schema.TryGetRole(schema.Prefix + "0000", out _));
        Assert.False(schema.TryGetRole(schema.Prefix + "abc", out _));
    }

    [Fact]
    public void RetailAndDemo_HaveSeparateIdentityGates()
    {
        var demo = Ff7Schema.ForTitle("PPSA18936")!;
        var retail = Ff7Schema.ForTitle("PPSA08664")!;
        Assert.NotEqual(demo.Variant, retail.Variant);
        Assert.False(demo.TryGetRole("ff7rebirthcommon", out _));
        Assert.False(retail.TryGetRole("ff7rebirthdemocommon", out _));
    }

    internal static byte[] Common(string title)
    {
        var remake = title == "PPSA01911";
        var bytes = new byte[1056];
        Write(0, 0x00010005);
        Write(4, 0x410);
        Write(8, remake ? 0U : 0x400U);
        Write(12, remake ? 0U : 1U);
        "COSDCOSDCOSDCOSD"u8.CopyTo(bytes.AsSpan(16));
        Write(32, 1);
        Write(36, 0x400);
        Write(0x410, 0x00010004);
        Write(0x414, remake ? 0U : 16U);
        Write(0x41C, remake ? 0U : 1U);
        return bytes;

        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }
}
