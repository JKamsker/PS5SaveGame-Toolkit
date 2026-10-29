using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class GroupPerformanceTests
{
    [Fact]
    public void ProposalsReadTheCommonAnchorOnlyForOneValidatedRoundTrip()
    {
        var set = CreateSet(globals: 2);
        var anchor = new CountingFile(set.Directories[0].Files[0]);
        var proposals = new XorJsonPlugin().ProposeGroups(anchor);
        Assert.Equal(2, proposals.Count);
        Assert.All(proposals, proposal => Assert.Empty(proposal.Errors));
        Assert.Equal(2, anchor.Reads); // Parse input once, compare validated no-op output once.
    }

    [Fact]
    public void GroupValidationDerivesRolesFromTheDocumentsItAlreadyOpened()
    {
        var set = CreateSet(globals: 1);
        var character = new CountingFile(set.Directories[0].Files[0]);
        var global = new CountingFile(set.Directories[1].Files[0]);
        var errors = new XorJsonPlugin().ValidateGroup(new([new("character", character), new("global", global)]), IReferenceContext.Empty);
        Assert.Empty(errors);
        Assert.Equal(2, character.Reads);
        Assert.Equal(2, global.Reads);
    }

    private static MemorySaveSet CreateSet(int globals) => new MemorySaveSet()
        .With("PPSA16064", "Character0", new Dictionary<string, byte[]>
        {
            ["hero.sav"] = Encoding.UTF8.GetBytes("{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\"},\"CharacterSaveV1\":{}}"),
        })
        .With("PPSA16064", "GlobalSaveDataDefault", Enumerable.Range(0, globals).ToDictionary(
            index => $"global{index}.sav", _ => XorJsonTests.Encode("{\"blobs\":[],\"number\":-0.000E+001}")));

    private sealed class CountingFile(ISaveFile inner) : ISaveFile
    {
        public int Reads { get; private set; }
        public string Id => inner.Id;
        public ISaveDirectory Directory => inner.Directory;
        public string RelativePath => inner.RelativePath;
        public string PathInDirectory => inner.PathInDirectory;
        public string FileName => inner.FileName;
        public long Length => inner.Length;
        public Stream OpenRead() => inner.OpenRead();
        public byte[] ReadHeader(int count) => inner.ReadHeader(count);
        public byte[] ReadAllBytes() { Reads++; return inner.ReadAllBytes(); }
    }
}
