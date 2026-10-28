using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class Bl4EvidenceTests
{
    [Fact]
    public void Bl4Sample_HasUnknownRoleAndCodecAndRemainsExcluded()
    {
        var files = SampleSaves.Load("PPSA01494").AllDiskFiles.Where(f => f.FileName == "gbxsavegame.sav").ToArray();
        var file = Assert.Single(files);
        var bytes = file.ReadAllBytes();
        Assert.Equal(1888, bytes.Length);
        Assert.False(bytes.AsSpan(0, 4).SequenceEqual("GVAS"u8));
        Assert.False(file.Directory.IsBackup);
        const int blockSize = 16;
        Assert.Equal(118, Enumerable.Range(0, bytes.Length / blockSize)
            .Select(i => Convert.ToHexString(bytes.AsSpan(i * blockSize, blockSize))).Distinct().Count());
        var plugin = new GearboxPlugin();
        Assert.False(plugin.Probe(file).IsMatch);
        Assert.DoesNotContain(plugin.Info.Games, g => g.TitleId == "PPSA01494");
    }
}
