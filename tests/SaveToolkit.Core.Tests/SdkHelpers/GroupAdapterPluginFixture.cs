using System.Text;
using SaveToolkit.Plugins.Gearbox;
using SaveToolkit.Plugins.XorJson;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.SdkHelpers;

internal sealed class GroupAdapterPluginFixture : IDisposable
{
    public IGamePlugin Plugin { get; }
    public DocumentGroupInput Input { get; }
    public IGroupedSaveDocument Group { get; }
    public bool Gearbox { get; }

    public GroupAdapterPluginFixture(string kind)
    {
        Gearbox = kind != "xorjson";
        Plugin = Gearbox ? new GearboxPlugin() : new XorJsonPlugin();
        if (Gearbox)
        {
            var set = SampleSaves.Load(kind);
            var anchor = set.AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup && f.Directory.Generation == 0);
            var proposal = Assert.Single(((IDocumentGroupPlugin)Plugin).ProposeGroups(anchor));
            Input = new(proposal.Members.Select(m => new DocumentGroupFile(m.Role, ((ISaveSet)set).FindFile(m.RelativePath)!)).ToArray());
        }
        else
        {
            var set = new MemorySaveSet()
                .With("PPSA16064", "Character0", new Dictionary<string, byte[]>
                {
                    ["hero.sav"] = Encoding.UTF8.GetBytes("""
                        {"SerializeMeta":{"HardFormat":"FCharacterSaveV1","SoftVersion":2,"FormatHash":3777917472},
                         "CharacterSaveV1":{"MetaData":{"IsOnline":false,"Level":1},"a~/b":7,"items":[ARRAY]}}
                        """.Replace("ARRAY", string.Join(',', Enumerable.Range(0, 100)), StringComparison.Ordinal)),
                })
                .With("PPSA16064", "GlobalSaveDataDefault", new Dictionary<string, byte[]>
                {
                    ["global.sav"] = Encoding.UTF8.GetBytes("""{"blobs":[],"volume":50,"a~/b":8,"items":[ARRAY]}"""
                        .Replace("ARRAY", string.Join(',', Enumerable.Range(0, 100)), StringComparison.Ordinal))
                        .Select(b => (byte)(b ^ 1)).ToArray(),
                });
            Input = new([new("character", set.Directories[0].Files[0]), new("global", set.Directories[1].Files[0])]);
        }
        Group = OpenGroup();
    }

    public IGroupedSaveDocument OpenGroup() => ((IDocumentGroupPlugin)Plugin).OpenGroup(Input, IReferenceContext.Empty);

    public static IEnumerable<ViewNode> Walk(ISaveDocument document, ViewNode node)
    {
        yield return node;
        // Raw Gearbox dumps are huge and irrelevant to the identity adapter.
        var children = node switch
        {
            SectionNode section when section.LazyId?.EndsWith("/raw", StringComparison.Ordinal) == true => [],
            SectionNode { LazyId: { } id } => document.ExpandNode(id),
            SectionNode section => section.Children,
            TabsNode tabs => tabs.Tabs.SelectMany(t => t.Children),
            TableNode table => table.Rows.SelectMany(r => r.Cells.Values),
            _ => [],
        };
        foreach (var child in children)
            foreach (var descendant in Walk(document, child)) yield return descendant;
    }

    public static Dictionary<string, byte[]> Bytes(ISaveDocument document) => document.Serialize()
        .ToDictionary(o => o.RelativePath, o => o.Content.ToArray(), StringComparer.Ordinal);

    public static void EqualBytes(IReadOnlyDictionary<string, byte[]> expected, ISaveDocument document)
    {
        var actual = Bytes(document);
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, actual[path]);
    }

    public void Dispose() => Group.Dispose();
}
