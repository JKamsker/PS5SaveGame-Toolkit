using System.Text;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class GroupProposalTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(17)]
    public void CharacterAnchorOnlyOffersItsOwnGlobalPartnerships(int characters)
    {
        var set = CreateSet(characters, globals: 1);
        var plugin = new XorJsonPlugin();

        foreach (var anchor in set.Directories.Take(characters).Select(directory => directory.Files.Single()))
        {
            var proposal = Assert.Single(plugin.ProposeGroups(anchor));
            Assert.Empty(proposal.Errors);
            Assert.Contains(proposal.Members, member => member.RelativePath == anchor.RelativePath);
            Assert.Equal(2, proposal.Members.Count);
        }
    }

    [Fact]
    public void BothAnchorRolesOnlyOfferPartnershipsContainingTheSelectedFile()
    {
        var set = CreateSet(characters: 2, globals: 2);
        var plugin = new XorJsonPlugin();
        foreach (var anchor in set.Directories.SelectMany(directory => directory.Files))
        {
            var proposals = plugin.ProposeGroups(anchor);
            Assert.Equal(2, proposals.Count);
            Assert.All(proposals, proposal =>
            {
                Assert.Empty(proposal.Errors);
                Assert.Contains(proposal.Members, member => member.RelativePath == anchor.RelativePath);
                Assert.Equal(2, proposal.Members.Count);
            });
        }
    }

    [Fact]
    public void GlobalAnchorStillRejectsTooManyActualPartners()
    {
        var set = CreateSet(characters: 17, globals: 1);
        var anchor = set.Directories.Last().Files.Single();
        var proposal = Assert.Single(new XorJsonPlugin().ProposeGroups(anchor));
        Assert.Contains(proposal.Errors, error => error.Message.Contains("16", StringComparison.Ordinal));
        Assert.Contains(proposal.Members, member => member.RelativePath == anchor.RelativePath);
    }

    private static MemorySaveSet CreateSet(int characters, int globals)
    {
        var set = new MemorySaveSet();
        var character = Encoding.UTF8.GetBytes("""
            {"SerializeMeta":{"HardFormat":"FCharacterSaveV1","SoftVersion":2,"FormatHash":3777917472},
             "CharacterSaveV1":{"MetaData":{"IsOnline":false}}}
            """);
        for (var i = 0; i < characters; i++)
            set.With("PPSA16064", $"Character{i}", new Dictionary<string, byte[]> { ["hero.sav"] = character });
        set.With("PPSA16064", "GlobalSaveDataDefault", Enumerable.Range(0, globals).ToDictionary(
            index => $"global{index}.sav", _ => XorJsonTests.Encode("{\"blobs\":[],\"volume\":50}")));
        return set;
    }
}
