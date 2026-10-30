using System.IO.Compression;
using System.Text;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Plugins.XorJson.Tests;

internal sealed class F13GroupSessionFixture : IDisposable
{
    public const string CharacterPath = "PPSA16064/sdimg_Character0/hero.sav";
    public const string GlobalPath = "PPSA16064/sdimg_GlobalSaveDataDefault/global.sav";
    public static readonly string Global = "{\"blobs\":[1,2],\"volume\":50,\"future\":1.2300E+02,\"rows\":[" +
        string.Join(',', Enumerable.Repeat("0", 100)) + "]}";
    public const string Character = """
        {"SerializeMeta":{"HardFormat":"FCharacterSaveV1","SoftVersion":2,"FormatHash":3777917472},
        "CharacterSaveV1":{"MetaData":{"IsOnline":false,"Level":1},"Ability":{"Attributes":[{"AttributeName":"Emeralds","CurrentValue":1}]},
        "Inventory":{"Entries":[{"ItemData":{"TypeTag":"SW.Item.HoneyBoots","RarityTag":"SW.Rarity.Common","TargetSlotOverride":"None"},"StackCount":1},
        {"ItemData":{"TypeTag":"SW.Item.Bow","RarityTag":"SW.Rarity.Rare","TargetSlotOverride":"Merchant"},"StackCount":1}]},"future":"\u0041","":1,"":2}}
        """;
    private readonly WorkspaceScratch _scratch = new();
    private readonly WorkspaceStore _store;
    private readonly Workspace _workspace;
    private readonly bool _xor;
    private readonly string _character;
    public DocumentSession Session { get; }

    public F13GroupSessionFixture(bool xor, bool characterReadOnly = false)
    {
        _xor = xor;
        _character = characterReadOnly ? Character.Replace("\"SoftVersion\":2", "\"SoftVersion\":1", StringComparison.Ordinal) : Character;
        var plugin = new XorJsonPlugin();
        _store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = _scratch.Root });
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = zip.CreateEntry(CharacterPath).Open()) entry.Write(Encoding.UTF8.GetBytes(_character));
            using (var entry = zip.CreateEntry(GlobalPath).Open()) entry.Write(XorJsonTests.Encode(Global, xor));
        }
        var bytes = buffer.ToArray();
        _workspace = _store.Create("f13-group", [new ImportSource("fixture.zip", () => new MemoryStream(bytes))]);
        var anchor = _workspace.Set.AllDiskFiles.Single(file => file.RelativePath == CharacterPath);
        var offer = Assert.Single(_workspace.ProposeGroups(anchor.Id, plugin.Info.Id));
        Session = _workspace.OpenGroup(offer.Token).Session;
        _ = Session.BuildView();
    }

    public void AssertBytes(string? character = null, string? global = null)
    {
        var plan = WorkspaceExporter.Plan(_workspace, new ExportOptions { Scope = ExportScope.Everything });
        using var buffer = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, buffer);
        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        Assert.Equal(new[] { CharacterPath, GlobalPath }.Order(StringComparer.Ordinal),
            zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        foreach (var (path, expected) in new[]
        {
            (CharacterPath, Encoding.UTF8.GetBytes(character ?? _character)), (GlobalPath, XorJsonTests.Encode(global ?? Global, _xor)),
        })
        {
            using var input = zip.GetEntry(path)!.Open();
            using var output = new MemoryStream();
            input.CopyTo(output);
            Assert.Equal(expected, output.ToArray());
        }
    }

    public void Dispose()
    {
        _store.Dispose();
        _scratch.Dispose();
    }
}
