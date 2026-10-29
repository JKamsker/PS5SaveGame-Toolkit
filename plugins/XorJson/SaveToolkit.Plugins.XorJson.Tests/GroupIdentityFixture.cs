using System.Text;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

internal sealed class GroupIdentityFixture : IDisposable
{
    public XorJsonPlugin Plugin { get; } = new();
    public DocumentGroupInput Input { get; }
    public IReferenceContext References { get; }
    public IGroupedSaveDocument Group { get; }

    public GroupIdentityFixture(bool xor)
    {
        var hero = """
            {"SerializeMeta":{"HardFormat":"FCharacterSaveV1","SoftVersion":2,"FormatHash":3777917472},
             "CharacterSaveV1":{"MetaData":{"IsOnline":false,"Level":1,"CurrentLocation":"SW.Region.Camp"},
             "Inventory":{"Entries":[{"ItemData":{"TypeTag":"SW.Item.Sword"},"StackCount":2}]},
             "quest":{"Quests":[{"QuestName":"CA00","State":"Completed"}]},"a~/b":7,"items":[ARRAY]}}
            """.Replace("ARRAY", string.Join(',', Enumerable.Range(0, 130)), StringComparison.Ordinal);
        var settings = """{"blobs":[],"volume":50,"future":-0.00E+02,"items":[ARRAY]}"""
            .Replace("ARRAY", string.Join(',', Enumerable.Range(0, 130)), StringComparison.Ordinal);
        var set = new MemorySaveSet()
            .With("PPSA16064", "Character0", new Dictionary<string, byte[]> { ["hero.sav"] = Encoding.UTF8.GetBytes(hero) })
            .With("PPSA16064", "GlobalSaveDataDefault", new Dictionary<string, byte[]> { ["global.sav"] = XorJsonTests.Encode(settings, xor) });
        Input = new([new("character", set.Directories[0].Files[0]), new("global", set.Directories[1].Files[0])]);
        References = new ReferencePackStore([Plugin]).Pin(Plugin, "PPSA16064");
        Group = Open();
    }

    public IGroupedSaveDocument Open() => Plugin.OpenGroup(Input, References);

    public void AssertOriginalBytes(ISaveDocument document)
    {
        var outputs = document.Serialize();
        Assert.Equal(Input.Members.Count, outputs.Count);
        foreach (var member in Input.Members)
            Assert.Equal(member.File.ReadAllBytes(), Assert.Single(outputs, output => output.RelativePath == member.File.RelativePath).Content.ToArray());
    }

    public void Dispose() => Group.Dispose();
}
