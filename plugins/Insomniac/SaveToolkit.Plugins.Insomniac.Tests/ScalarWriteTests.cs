using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class ScalarWriteTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EveryGameplayWritePersistsChangesOnlyItsAtomAndSupportsUndoRedo(int generation) =>
        CheckEveryWrite(PlayerCorpusTests.Find("i29slot0-s-autosave", generation), expectedCount: 9);

    [Fact]
    public void EveryBooleanPreferenceWritePersistsChangesOnlyItsAtomAndSupportsUndoRedo()
    {
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles
            .Where(f => f.FileName == "i29-userprefs.save" && !f.Directory.IsBackup).ToArray();
        Assert.Equal(2, files.Length);
        foreach (var file in files) CheckEveryWrite(file, expectedCount: 61);
    }

    [Fact]
    public void EveryPrimaryGameplayGenerationSupportsAtomicBatchUndoRedoWithinItsWidths()
    {
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles
            .Where(f => f.FileName == "i29-s.save" && !f.Directory.IsBackup).ToArray();
        Assert.Equal(36, files.Length);
        Assert.Equal(RiftScalarWrites.ObservedCareerBoltsMaximum,
            files.Max(f => PlayerCorpusTests.Fields(f, RiftPlayerView.CampaignTag)["CareerBolts"].Values[0].AsInt64()));
        Assert.Equal(RiftScalarWrites.ObservedSpentBoltsMaximum,
            files.Max(f => PlayerCorpusTests.Fields(f, RiftPlayerView.CampaignTag)["SpentBolts"].Values[0].AsInt64()));
        foreach (var file in files)
        {
            var plugin = new InsomniacPlugin();
            using var document = plugin.Open(file);
            using var session = new DocumentSession("batch", file, plugin, document);
            var fields = Fields(session.BuildView()).Where(f => !f.Spec.ReadOnly).ToArray();
            Assert.Equal(9, fields.Length);
            var edits = fields.Select(f => new ValueEdit(f.ValueId, Next(f))).ToArray();
            Assert.True(session.ApplyEdits(edits.Select(Incoming).ToArray()).Ok);
            var patched = Assert.Single(document.Serialize()).Content.ToArray();
            Assert.NotEqual(file.ReadAllBytes(), patched);
            var roots = new DdlSections(Dat1Container.Parse(file.ReadAllBytes())).Roots;
            var intended = new HashSet<int>();
            foreach (var field in fields)
            {
                var tag = field.Spec is BoolSpec ? RiftPlayerView.GoldBoltTag : RiftPlayerView.CampaignTag;
                var atom = roots[tag][field.ValueId.Split('/')[1]];
                var width = atom.Kind switch { DdlValueKind.UInt16 => 2, DdlValueKind.UInt32 => 4, _ => 1 };
                intended.UnionWith(Enumerable.Range(atom.Offset, width));
            }
            var original = file.ReadAllBytes();
            Assert.Equal(original.Length, patched.Length);
            Assert.All(Enumerable.Range(0, original.Length).Where(i => original[i] != patched[i]), i => Assert.Contains(i, intended));
            using var reopened = plugin.Open(MemorySaveSet.SingleFile(file.FileName, patched, "PPSA01474"));
            _ = reopened.BuildView();
            Assert.All(edits, edit => Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId)));
            Assert.True(session.Undo().Ok);
            Assert.False(session.State.CanUndo);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(patched, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void BoltBatchesValidateTheFinalBalanceAndReportLiveDiagnosticAndCalculatedChanges()
    {
        var file = Assert.IsType<DiskSaveFile>(PlayerCorpusTests.Find("i29slot0-s-autosave", 1));
        var plugin = new InsomniacPlugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("balance", file, plugin, document);
        _ = session.BuildView();
        _ = session.Expand("system/d436a987");
        _ = session.Expand("ddl/d436a987/CareerBolts");
        var rejected = session.ApplyEdits([Incoming(new("player/CareerBolts", FieldValue.Of(1L)))]);
        Assert.False(rejected.Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        // Each intermediate state would be negative; the final pair is valid in either order.
        var result = session.ApplyEdits([
            Incoming(new("player/CareerBolts", FieldValue.Of(100L))),
            Incoming(new("player/SpentBolts", FieldValue.Of(50L))),
        ]);
        Assert.True(result.Ok);
        Assert.Contains(result.Changes, c => c.ValueId == "player/AvailableBolts" && c.NewValue.AsInt64() == 50);
        Assert.Contains(result.Changes, c => c.ValueId == "ddl/d436a987/CareerBolts/0" && c.NewValue.AsInt64() == 100);
        Assert.Equal(100, document.GetValue("ddl/d436a987/CareerBolts/0").AsInt64());
        var rebuilt = Fields(document.BuildView()).ToDictionary(f => f.ValueId);
        Assert.Equal(100, rebuilt["player/CareerBolts"].Value.AsInt64());
        Assert.Equal(50, rebuilt["player/AvailableBolts"].Value.AsInt64());
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(50, document.GetValue("player/AvailableBolts").AsInt64());
    }

    [Fact]
    public void ForeignTitleKeepsMatchingDat1ReadOnly()
    {
        var bytes = PlayerCorpusTests.Find("i29slot0-s-autosave", 0).ReadAllBytes();
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("i29-s.save", bytes));
        Assert.False(document.CanEdit);
        Assert.All(Fields(document.BuildView()), f => Assert.True(f.Spec.ReadOnly));
    }

    [Fact]
    public void AboveCorpusInputRemainsUnchangedAndCanBeRestoredWithoutWideningItsPolicy()
    {
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        var bytes = file.ReadAllBytes();
        var offset = PlayerCorpusTests.Fields(file, RiftPlayerView.CampaignTag)["CareerBolts"].Offset;
        var original = RiftScalarWrites.ObservedCareerBoltsMaximum + 1;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), checked((uint)original));
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("i29-s.save", bytes, "PPSA01474"));
        var field = Assert.Single(Fields(document.BuildView()), f => f.ValueId == "player/CareerBolts");
        Assert.Equal(original, Assert.IsType<IntegerSpec>(field.Spec).Max);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(original + 1))]).Succeeded);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(original - 1))]).Succeeded);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(original))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void SerializationRejectsMalformedOutputInsteadOfTrustingTheOpenedModel()
    {
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        var container = Dat1Container.Parse(file.ReadAllBytes());
        using var document = new InsomniacDocument(file.RelativePath, container, file.FileName, IReferenceContext.Empty, true);
        var field = new DdlSections(container).Roots[RiftPlayerView.GoldBoltTag]["RPGEnabled"];
        container.Bytes[field.Offset] = 2;
        Assert.Throws<SaveFormatException>(() => document.Serialize());
    }

    private static void CheckEveryWrite(ISaveFile input, int? expectedCount = null)
    {
        var file = Assert.IsType<DiskSaveFile>(input);
        var plugin = new InsomniacPlugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("scalar", file, plugin, document);
        var fields = Fields(session.BuildView()).Where(f => !f.Spec.ReadOnly).ToArray();
        Assert.NotEmpty(fields);
        if (expectedCount is { } count) Assert.Equal(count, fields.Length);
        var original = file.ReadAllBytes();
        var roots = new DdlSections(Dat1Container.Parse(original)).Roots;
        foreach (var field in fields)
        {
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Contains("game may reject", field.Warning);
            Assert.Contains("Not validated on console", field.Description);
            var invalids = new List<FieldValue> { FieldValue.Null, FieldValue.Of("invalid"), FieldValue.Of(double.NaN) };
            if (field.Spec is IntegerSpec bounds)
                invalids.AddRange([FieldValue.Of(bounds.Min - 1), FieldValue.Of(bounds.Max + 1), FieldValue.Of(true)]);
            else invalids.Add(FieldValue.Of(2L));
            foreach (var invalid in invalids)
            {
                Assert.False(document.ApplyEdits([new(field.ValueId, invalid)]).Succeeded);
                Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            }
            var edit = new ValueEdit(field.ValueId, Next(field));
            Assert.True(session.ApplyEdits([Incoming(edit)]).Ok);
            Assert.Equal(edit.Value, document.GetValue(edit.ValueId));
            var patched = Assert.Single(document.Serialize()).Content.ToArray();
            var name = field.ValueId.Split('/')[1];
            var tag = field.ValueId.StartsWith("preferences/", StringComparison.Ordinal) ? DdlSections.PreferencesTag :
                field.Spec is BoolSpec ? RiftPlayerView.GoldBoltTag : RiftPlayerView.CampaignTag;
            var atom = roots[tag][name];
            var width = atom.Kind switch { DdlValueKind.UInt16 => 2, DdlValueKind.UInt32 => 4, DdlValueKind.UInt64 => 8, _ => 1 };
            Assert.Equal(original.Length, patched.Length);
            var changes = Enumerable.Range(0, original.Length).Where(i => original[i] != patched[i]).ToArray();
            Assert.NotEmpty(changes);
            Assert.All(changes, offset => Assert.InRange(offset, atom.Offset, atom.Offset + width - 1));
            var candidate = MemorySaveSet.SingleFile(file.FileName, patched, "PPSA01474");
            var decoder = new RiftEvidenceDecoder();
            var diff = SemanticDiffer.Compare(decoder.Decode(file, new()), decoder.Decode(candidate, new()), maxChanges: 10000);
            Assert.False(diff.InputTruncated);
            Assert.False(diff.Truncated);
            Assert.Equal(SemanticChangeKind.Changed, Assert.Single(diff.Changes).Kind);
            using var reopened = plugin.Open(candidate);
            _ = reopened.BuildView();
            Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId));
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(patched, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Undo().Ok);
        }
    }

    private static FieldValue Next(FieldNode field) => field.Spec switch
    {
        BoolSpec => FieldValue.Of(!field.Value.AsBool()),
        IntegerSpec bounds => FieldValue.Of(field.Value.AsInteger() < bounds.Max
            ? field.Value.AsInteger() + 1 : field.Value.AsInteger() - 1),
        _ => throw new InvalidOperationException("Unexpected writable spec."),
    };

    private static IncomingEdit Incoming(ValueEdit edit) => new(edit.ValueId, JsonSerializer.SerializeToElement(edit.Value));

    private static IEnumerable<FieldNode> Fields(ViewNode node) => node switch
    {
        FieldNode field => [field],
        TabsNode tabs => tabs.Tabs.SelectMany(t => t.Children).SelectMany(Fields),
        SectionNode section => section.Children.SelectMany(Fields),
        _ => [],
    };
}
