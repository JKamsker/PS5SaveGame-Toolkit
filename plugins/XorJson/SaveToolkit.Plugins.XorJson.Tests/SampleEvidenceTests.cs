using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class SampleEvidenceTests
{
    [Fact]
    public void F1ConfirmsOneGenerationAndDecodesBothRolesWithoutInventingADifferential()
    {
        var sample = SampleSaves.Load("PPSA16064");
        var files = sample.AllDiskFiles.Where(f => !f.IsSystemFile).ToArray();
        Assert.Equal(2, files.Length);
        Assert.Single(files.Select(f => f.Directory.Generation).Distinct());
        var decoder = new HookSaveDecoder(new XorJsonPlugin().Decoders.Single());
        foreach (var file in files)
        {
            var tree = decoder.Decode(file, new DecodeLimits());
            Assert.False(tree.Truncated);
            Assert.NotEmpty(tree.Values);
        }
        var report = SaveSetDiffer.Compare(sample, sample, new SaveDiffOptions
        {
            BeforeGeneration = GenerationSelector.Only(0), AfterGeneration = GenerationSelector.Latest,
            Decoder = decoder,
        });
        Assert.All(report.Files, f => Assert.Equal(FileDiffStatus.Unchanged, f.Status));
    }

    [Fact]
    public void F1IdentifiesExactlyTheEditedSettingsTokenAndUndoRestoresSampleBytes()
    {
        var sample = SampleSaves.Load("PPSA16064");
        var file = sample.AllDiskFiles.Single(f => !f.IsSystemFile && f.Directory.Name == "GlobalSaveDataDefault");
        using var document = new XorJsonPlugin().Open(file);
        var volume = XorJsonTests.Fields(document).Single(f => f.Label == "masterVolume");
        Assert.Equal("50", volume.Value.AsExactNumber());
        Assert.True(document.ApplyEdits([new(volume.ValueId, FieldValue.ExactNumber("49.000E+00"))]).Succeeded);
        var outputs = document.Serialize();
        var replacements = outputs.ToDictionary(output => output.RelativePath, output => output.Content);
        var after = new OverlaySaveSet(sample, replacements);
        var character = sample.AllDiskFiles.Single(f => !f.IsSystemFile && f.Directory.Name.StartsWith("Character", StringComparison.Ordinal));
        Assert.Equal(character.ReadAllBytes(), after.Directories.SelectMany(directory => directory.Files)
            .Single(f => f.RelativePath == character.RelativePath).ReadAllBytes());
        var report = SaveSetDiffer.Compare(sample, after, new SaveDiffOptions { Decoder = new HookSaveDecoder(new XorJsonPlugin().Decoders.Single()) });
        var changed = Assert.Single(report.Files, f => f.Status == FileDiffStatus.Changed);
        Assert.NotNull(changed.Semantic);
        Assert.Null(changed.Semantic.Error);
        var delta = Assert.Single(changed.Semantic.Changes);
        Assert.Equal("blobs[name=Audio]/masterVolume", delta.Path);
        Assert.Equal("50", delta.Before);
        Assert.Equal("49.000E+00", delta.After);
        Assert.Contains(report.Files, f => f.Status == FileDiffStatus.Unchanged);
        Assert.True(document.ApplyEdits([new(volume.ValueId, volume.Value)]).Succeeded);
        Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void CharacterShowsObservedMetadataInventoryAndProgressionWithExperimentalWrites()
    {
        var sample = SampleSaves.Load("PPSA16064");
        var file = sample.AllDiskFiles.Single(f => !f.IsSystemFile && f.Directory.Name.StartsWith("Character", StringComparison.Ordinal));
        var plugin = new XorJsonPlugin();
        Assert.Equal(ProbeScore.Engine, plugin.Probe(file).Score);
        using var document = plugin.Open(file);
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        Assert.Equal(["Overview", "Hero", "Inventory", "Quests", "World", "Collections", "Raw / Unknown"], tabs.Tabs.Select(t => t.Title));
        var fields = XorJsonTests.Fields(document);
        Assert.All(fields, field => Assert.False(field.Spec.ReadOnly));
        Assert.Equal("2", fields.Single(f => f.Label == "SoftVersion").Value.AsExactNumber());
        Assert.Equal("3777917472", fields.Single(f => f.Label == "FormatHash").Value.AsExactNumber());
        Assert.Equal(5, fields.Count(f => f.Label == "TypeTag" && f.Description!.Contains("member(\"Inventory\",0)", StringComparison.Ordinal)));
        using var json = System.Text.Json.JsonDocument.Parse(file.ReadAllBytes());
        Assert.Equal(9, json.RootElement.GetProperty("CharacterSaveV1").GetProperty("Ability").GetProperty("Attributes").GetArrayLength());
        Assert.True(document.ApplyEdits([new(fields[0].ValueId, fields[0].Value)]).Succeeded);
        Assert.Throws<SaveFormatException>(() => document.InvokeAction("json/add/0", new Dictionary<string, FieldValue>()));
        Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void SettingsExposeSevenNamedBlobsAndExposeWarnedStructuralWrites()
    {
        var file = SampleSaves.Load("PPSA16064").AllDiskFiles.Single(f => !f.IsSystemFile && f.Directory.Name == "GlobalSaveDataDefault");
        using var document = new XorJsonPlugin().Open(file);
        var nodes = StructuralTests.Nodes(document);
        var titles = nodes.OfType<SectionNode>().Select(s => s.Title).ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[] { "Audio", "General", "Gfx", "Controls", "KeyBindings", "Interface", "Accessibility" })
            Assert.Contains(expected, titles);
        Assert.Contains(nodes.OfType<ActionNode>(), action => action.ActionId == "json/bulk-set");
        Assert.Contains(nodes.OfType<ActionNode>(), action => action.Label == "Remove row/member");
        Assert.Throws<SaveFormatException>(() => document.InvokeAction("json/remove/0", new Dictionary<string, FieldValue>()));
        Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData("2", "3777917472", false)]
    [InlineData("5", "3777917472", false)]
    [InlineData("2", "1", false)]
    [InlineData("2", "3777917472", true)]
    public void VersionAndOnlineGatesCannotEnableWrites(string version, string hash, bool online)
    {
        var text = "{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\",\"SoftVersion\":" + version +
            ",\"FormatHash\":" + hash + "},\"CharacterSaveV1\":{\"MetaData\":{\"IsOnline\":" + (online ? "true" : "false") + ",\"Level\":1}}}";
        var file = MemorySaveSet.SingleFile("hero.sav", System.Text.Encoding.UTF8.GetBytes(text));
        var plugin = new XorJsonPlugin();
        Assert.True(plugin.Probe(file).IsMatch);
        using var document = plugin.Open(file);
        var writable = version == "2" && hash == "3777917472" && !online;
        Assert.Equal(writable, document.CanEdit);
        Assert.All(XorJsonTests.Fields(document), f => Assert.Equal(!writable, f.Spec.ReadOnly));
        Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
        if (!writable)
        {
            if (version != "2" || hash != "3777917472")
                Assert.Contains(StructuralTests.Nodes(document).OfType<TextNode>(), n => n.Text.Contains("Untested", StringComparison.Ordinal));
            Assert.Throws<SaveFormatException>(() => document.InvokeAction("json/remove/0", new Dictionary<string, FieldValue>()));
        }
    }

    [Theory]
    [InlineData("{\"SerializeMeta\":{\"HardFormat\":\"Other\"},\"CharacterSaveV1\":{}}")]
    [InlineData("{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\"},\"CharacterSaveV1\":[]}")]
    [InlineData("{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\"},\"CharacterSaveV1\":{},\"CharacterSaveV1\":{}}")]
    public void ForeignAndAmbiguousPlainCharacterShapesAreDeclined(string text)
    {
        var file = MemorySaveSet.SingleFile("hero.sav", System.Text.Encoding.UTF8.GetBytes(text));
        Assert.False(new XorJsonPlugin().Probe(file).IsMatch);
        Assert.Throws<SaveFormatException>(() => new XorJsonPlugin().Open(file));
    }
}
