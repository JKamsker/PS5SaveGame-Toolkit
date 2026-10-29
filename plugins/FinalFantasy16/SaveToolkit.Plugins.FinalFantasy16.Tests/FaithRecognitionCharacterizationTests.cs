using System.Buffers.Binary;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithRecognitionCharacterizationTests
{
    [Theory]
    [InlineData("game-save-data", false, "Manual/special gameplay save")]
    [InlineData("game-save-data00", false, "Autosave 00")]
    [InlineData("game-save-data01", false, "Autosave 01")]
    [InlineData("game-save-data09", false, "Autosave 09")]
    [InlineData("system-save-data", true, "System/settings")]
    [InlineData("game-save-data10", false, null)]
    [InlineData("game-save-data0", false, null)]
    [InlineData("game-save-data0A", false, null)]
    [InlineData("game-save-data000", false, null)]
    [InlineData("Game-save-data00", false, null)]
    [InlineData("game-save-data0９", false, null)]
    [InlineData("param.sfo", false, null)]
    public void ProbeAndDecoderRetainExactNamesRolesAndLabels(string name, bool system, string? label)
    {
        var plugin = new FinalFantasy16Plugin();
        foreach (var title in new[] { "PPSA10665", "PPSA13803", "PPSA01288", "ppsa10665" })
        foreach (var matchingRole in new[] { true, false })
        {
            var bytes = FaithTestData.Container((99, 1, new byte[8]));
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), system == matchingRole
                ? FaithContainer.SystemVersion : FaithContainer.GameVersion);
            var file = FaithTestData.File(bytes, title, name);
            var expected = label is not null && matchingRole && title is "PPSA10665" or "PPSA13803";
            var probe = plugin.Probe(file);
            Assert.Equal(expected, probe.IsMatch);
            Assert.Equal(expected, Assert.Single(plugin.Decoders).CanDecode(file));
            if (expected) Assert.Equal(label, probe.Label);
        }
        Assert.Equal("ffxvi-evidence", Assert.Single(plugin.Decoders).Id);
    }

    [Fact]
    public void ShortHeadersAndUnknownVersionsAreNotRecognized()
    {
        var plugin = new FinalFantasy16Plugin();
        var bytes = FaithTestData.Container((99, 1, []));
        for (var length = 0; length < 16; length++)
        {
            var file = FaithTestData.File(bytes[..length]);
            Assert.False(plugin.Probe(file).IsMatch);
            Assert.False(Assert.Single(plugin.Decoders).CanDecode(file));
        }
        foreach (var offset in new[] { 0, 8 })
        {
            var invalid = bytes.ToArray();
            invalid[offset] ^= 1;
            var file = FaithTestData.File(invalid);
            Assert.False(plugin.Probe(file).IsMatch);
            Assert.False(Assert.Single(plugin.Decoders).CanDecode(file));
        }
    }
}
