using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class CorruptionTests
{
    [Theory]
    [InlineData("magic")]
    [InlineData("sentinel")]
    [InlineData("profile-version")]
    [InlineData("menu-overflow")]
    [InlineData("active-flag")]
    [InlineData("empty-active-slot")]
    [InlineData("profile-name")]
    [InlineData("profile-level")]
    [InlineData("profile-seconds")]
    [InlineData("profile-history")]
    [InlineData("attribute-range")]
    [InlineData("attribute-sum")]
    [InlineData("level-range")]
    [InlineData("held-above-history")]
    [InlineData("held-above-game-cap")]
    [InlineData("unterminated-name")]
    [InlineData("invalid-utf16")]
    [InlineData("wrong-item-layout")]
    public void InconsistentOrUnsupportedData_ThrowsSaveFormatException(string mutation)
    {
        var fixture = new SyntheticSave([0]);
        var player = fixture.Players[0];
        var profile = fixture.Profile(0);
        switch (mutation)
        {
            case "magic": fixture.Write32(0, 0); break;
            case "sentinel": fixture.Write32(8, 0); break;
            case "profile-version": fixture.Write32(SyntheticSave.UserData, 0); break;
            case "menu-overflow": fixture.Write32(SyntheticSave.Menu + sizeof(uint), uint.MaxValue); break;
            case "active-flag": fixture.Bytes[fixture.ActiveOffset] = 2; break;
            case "empty-active-slot": fixture.Write32(SyntheticSave.Slot(0), 0); break;
            case "profile-name": fixture.Bytes[profile] ^= 1; break;
            case "profile-level": fixture.Write32(profile + SyntheticSave.ProfileLevel, 2); break;
            case "profile-seconds": fixture.Write32(profile + SyntheticSave.ProfileSeconds, 0); break;
            case "profile-history": fixture.Write32(profile + SyntheticSave.ProfileMemory, 0); break;
            case "attribute-range": fixture.Write32(player + SyntheticSave.Attributes, 100); break;
            case "attribute-sum": fixture.Write32(player + SyntheticSave.Attributes, 11); break;
            case "level-range":
                fixture.Write32(player + SyntheticSave.Level, 714);
                fixture.Write32(profile + SyntheticSave.ProfileLevel, 714);
                break;
            case "held-above-history": fixture.Write32(player + SyntheticSave.Runes, SyntheticSave.LifetimeRunes + 1); break;
            case "held-above-game-cap":
                fixture.Write32(player + SyntheticSave.Runes, 1_000_000_000);
                fixture.Write32(player + SyntheticSave.RuneMemory, uint.MaxValue);
                fixture.Write32(profile + SyntheticSave.ProfileMemory, uint.MaxValue);
                break;
            case "unterminated-name": fixture.Bytes.AsSpan(player + SyntheticSave.Name, SyntheticSave.NameBytes).Fill(0x41); break;
            case "invalid-utf16": fixture.Bytes[player + SyntheticSave.Name + 1] = 0xD8; break;
            case "wrong-item-layout": fixture.Write32(player - sizeof(uint), 0x1000); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Assert.Throws<SaveFormatException>(() => fixture.Open());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(SyntheticSave.HeaderSize)]
    [InlineData(SyntheticSave.UserData)]
    [InlineData(SyntheticSave.FileSize - 1)]
    public void TruncationAtAnyAllocationBoundary_IsRejected(int length)
    {
        var fixture = new SyntheticSave([0]);
        var file = MemorySaveSet.SingleFile("memory.dat", fixture.Bytes[..length], "PPSA03169", "sce_sdmemory");
        var plugin = new EldenRingPlugin();
        Assert.False(plugin.Probe(file).IsMatch);
        Assert.Throws<SaveFormatException>(() => plugin.Open(file));
    }
}
