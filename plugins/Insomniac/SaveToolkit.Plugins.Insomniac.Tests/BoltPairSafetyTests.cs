using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class BoltPairSafetyTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void ImportedNegativeBalanceCanBeRestoredExactlyThroughScalarAndPlannedHistory(int generation, bool planned)
    {
        // Accepted-input robustness fixture, not evidence of a console-produced negative balance.
        var source = PlayerCorpusTests.Find("i29slot0-s-autosave", generation);
        var bytes = source.ReadAllBytes();
        var fields = PlayerCorpusTests.Fields(source, RiftPlayerView.CampaignTag);
        var spent = fields["SpentBolts"].Values[0].AsUInt32();
        const uint importedDeficit = 10;
        Store(bytes, fields["CareerBolts"], spent - importedDeficit);
        var file = Import(bytes);
        var plugin = new InsomniacPlugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("negative-balance", file, plugin, document);
        _ = session.BuildView();
        _ = session.Expand("system/d436a987");
        _ = session.Expand("ddl/d436a987/CareerBolts");
        _ = session.Expand("ddl/d436a987/SpentBolts");
        Assert.Equal(-(long)importedDeficit, document.GetValue(RiftScalarWrites.BalanceId).AsInt64());
        Assert.Equal(bytes, Output(document));

        SessionEditResult applied;
        if (planned)
        {
            var preview = session.PlanAction(RiftBoltPlanning.ActionId,
                new Dictionary<string, JsonElement> { ["target"] = JsonSerializer.SerializeToElement(FieldValue.Of(1L)) });
            Assert.Empty(preview.Plan.Errors);
            Assert.Equal(bytes, Output(document));
            Assert.Contains(preview.Plan.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
            applied = session.ApplyActionPlan(preview.PlanId!, preview.Revision);
        }
        else
        {
            applied = session.ApplyEdits([
                Incoming(RiftScalarWrites.CareerId, spent), Incoming(RiftScalarWrites.SpentId, spent - 1L)]);
        }
        Assert.True(applied.Ok);
        Assert.Equal(1, document.GetValue(RiftScalarWrites.BalanceId).AsInt64());
        var patched = Output(document);
        Assert.NotEqual(bytes, patched);
        var writableOffsets = Enumerable.Range(fields["CareerBolts"].Offset, Width(fields["CareerBolts"])).ToHashSet();
        if (!planned) writableOffsets.UnionWith(Enumerable.Range(fields["SpentBolts"].Offset, Width(fields["SpentBolts"])));
        Assert.All(Enumerable.Range(0, bytes.Length).Where(index => bytes[index] != patched[index]),
            index => Assert.Contains(index, writableOffsets));
        using (var reopened = plugin.Open(MemorySaveSet.SingleFile("i29-s.save", patched, "PPSA01474")))
        {
            _ = reopened.BuildView();
            Assert.Equal(1, reopened.GetValue(RiftScalarWrites.BalanceId).AsInt64());
        }

        // The original exception must not authorize a different negative pair or partial batch.
        var option = document.GetValue("player/RPGEnabled");
        var state = session.State;
        foreach (var invalid in new[]
        {
            new[] { Incoming(RiftScalarWrites.CareerId, spent - importedDeficit - 1L) },
            new[] { Incoming(RiftScalarWrites.CareerId, spent - importedDeficit), Incoming(RiftScalarWrites.SpentId, spent - 1L) },
        })
        {
            var rejected = session.ApplyEdits([
                new("player/RPGEnabled", JsonSerializer.SerializeToElement(FieldValue.Of(!option.AsBool()))), .. invalid]);
            Assert.False(rejected.Ok);
            Assert.Empty(rejected.Changes);
            Assert.Equal(state, session.State);
            Assert.Equal(patched, Output(document));
        }

        for (var cycle = 0; cycle < 2; cycle++)
        {
            var undone = session.Undo();
            Assert.True(undone.Ok, string.Join("; ", undone.Errors.Select(error => error.Message)));
            Assert.False(session.State.IsDirty);
            Assert.False(session.State.CanUndo);
            Assert.True(session.State.CanRedo);
            Assert.Equal(bytes, Output(document));
            Assert.Equal(-(long)importedDeficit, document.GetValue(RiftScalarWrites.BalanceId).AsInt64());
            Assert.Equal(spent - importedDeficit, document.GetValue("ddl/d436a987/CareerBolts/0").AsUInt32());
            Assert.Equal(spent, document.GetValue("ddl/d436a987/SpentBolts/0").AsUInt32());
            Assert.True(session.Redo().Ok);
            Assert.True(session.State.IsDirty);
            Assert.Equal(patched, Output(document));
        }
        Assert.True(session.Undo().Ok);
        Assert.Equal(bytes, Output(document));
    }

    private static void Store(byte[] bytes, DdlField field, uint value)
    {
        if (field.Kind == DdlValueKind.UInt16)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(field.Offset), checked((ushort)value));
        else
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(field.Offset), value);
    }

    private static int Width(DdlField field) => field.Kind == DdlValueKind.UInt16 ? sizeof(ushort) : sizeof(uint);
    private static byte[] Output(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
    private static IncomingEdit Incoming(string id, long value) => new(id, JsonSerializer.SerializeToElement(FieldValue.Of(value)));

    private static DiskSaveFile Import(byte[] bytes)
    {
        var root = Path.Combine(TestScratch.ProcessFolder, Guid.NewGuid().ToString("N"));
        var set = SaveSetImporter.Import([new ImportSource("PPSA01474/sdimg_i29slot0-s-autosave/i29-s.save",
            () => new MemoryStream(bytes, writable: false))], root);
        return Assert.Single(set.AllDiskFiles);
    }
}
