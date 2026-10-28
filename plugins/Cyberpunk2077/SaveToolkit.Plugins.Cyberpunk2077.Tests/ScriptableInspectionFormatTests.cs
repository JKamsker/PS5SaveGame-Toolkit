using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ScriptableInspectionFormatTests
{
    [Fact]
    public void ModernPerkGroupsDecodeRanksAndKeepOmittedZeroDefaultsDistinctFromLegacyAreas()
    {
        var fixture = new InspectionPackageFixture();
        var perk = fixture.Class(("type", "gamedataNewPerkType", fixture.Enum("Cool_Left_Perk_1_1")),
            ("currLevel", "Int32", BitConverter.GetBytes(2)));
        var group = fixture.Class(("type", "gamedataAttributeDataType", fixture.Enum("CoolAttributeData")),
            ("unlockedPerks", "array:SNewPerk", InspectionPackageFixture.Array(perk, fixture.Class())));
        var root = fixture.Class(("attributesData", "array:SAttributeData", InspectionPackageFixture.Array(group, fixture.Class())));
        var reader = fixture.Reader(root);
        var groups = reader.Array(reader.Package.Chunk(0), "attributesData", "SAttributeData");
        Assert.Equal(2, groups.Count);
        Assert.Equal("CoolAttributeData", groups[0].Enum("type", "gamedataAttributeDataType", "BodyAttributeData"));
        Assert.Equal("BodyAttributeData", groups[1].Enum("type", "gamedataAttributeDataType", "BodyAttributeData"));
        var perks = reader.Array(groups[0], "unlockedPerks", "SNewPerk");
        Assert.Equal(2, perks.Count);
        Assert.Equal(2, perks[0].Int("currLevel"));
        Assert.Equal("Cool_Left_Perk_1_1", perks[0].Enum("type", "gamedataNewPerkType", "Body_Central_Milestone_1"));
        Assert.Equal("Body_Central_Milestone_1", perks[1].Enum("type", "gamedataNewPerkType", "Body_Central_Milestone_1"));
        Assert.Empty(reader.Array(groups[1], "unlockedPerks", "SNewPerk"));
    }

    [Fact]
    public void NestedInspectionRejectsCountOverflowUnexplainedSuffixAndUnsupportedMemberTypes()
    {
        var fixture = new InspectionPackageFixture();
        var root = fixture.Class(("attributesData", "array:SAttributeData", new byte[] { 0xFF, 0xFF, 0xFF, 0x7F }));
        var reader = fixture.Reader(root);
        Assert.Throws<SaveFormatException>(() => reader.Array(reader.Package.Chunk(0), "attributesData", "SAttributeData"));
        root = fixture.Class(("attributesData", "array:SAttributeData", new byte[] { 0, 0, 0, 0, 0xCC }));
        reader = fixture.Reader(root);
        Assert.Throws<SaveFormatException>(() => reader.Array(reader.Package.Chunk(0), "attributesData", "SAttributeData"));
        var opaque = fixture.Class(("opaque", "UnknownFutureType", new byte[] { 1 }));
        root = fixture.Class(("attributesData", "array:SAttributeData", InspectionPackageFixture.Array(opaque)));
        reader = fixture.Reader(root);
        Assert.Throws<SaveFormatException>(() => reader.Array(reader.Package.Chunk(0), "attributesData", "SAttributeData"));
    }

    [Fact]
    public void OutOfBoundsAndMistypedHandlesAreRejectedBeforeFollowingThem()
    {
        var fixture = new InspectionPackageFixture();
        var root = fixture.Class(("ownerData", "array:handle:EquipmentSystemPlayerData", new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }));
        var reader = fixture.Reader(root); // Handle 0 is in bounds but is FixtureRoot, not player data.
        Assert.Throws<SaveFormatException>(() => reader.Handles(reader.Package.Chunk(0), "ownerData", "EquipmentSystemPlayerData"));
        root = fixture.Class(("ownerData", "array:handle:EquipmentSystemPlayerData", new byte[] { 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F }));
        reader = fixture.Reader(root);
        Assert.Throws<SaveFormatException>(() => reader.Handles(reader.Package.Chunk(0), "ownerData", "EquipmentSystemPlayerData"));
    }
}
