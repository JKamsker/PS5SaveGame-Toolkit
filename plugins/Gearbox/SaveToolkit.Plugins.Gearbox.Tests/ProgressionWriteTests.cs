using System.Text.Json;
using Google.Protobuf;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ProgressionWriteTests
{
    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void SduAndDifficulty_EditReopenRejectInvalidAndUndoRedoEveryGeneration(string title)
    {
        var plugin = new GearboxPlugin();
        var tested = new HashSet<string>();
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f => plugin.Probe(f).IsMatch && !f.Directory.IsBackup))
        {
            using var document = (GearboxDocument)plugin.Open(file);
            using var session = new DocumentSession("progression", file, plugin, document);
            _ = session.BuildView();
            var fields = new List<FieldNode>();
            fields.AddRange(session.Expand("sdus").OfType<TableNode>().SelectMany(t => t.Rows.SelectMany(r => r.Cells.Values)).OfType<FieldNode>());
            if (!document.Profile) fields.AddRange(session.Expand("difficulty").OfType<FieldNode>());
            foreach (var field in fields.Where(f => !f.Spec.ReadOnly))
            {
                var old = field.Value.AsInt64();
                var spec = Assert.IsType<IntegerSpec>(field.Spec);
                var maximum = checked((long)spec.Max);
                var next = old == maximum ? old - 1 : maximum;
                var original = document.Serialize().Single().Content.ToArray();
                var changed = session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(next.ToString(System.Globalization.CultureInfo.InvariantCulture)))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(changed.Ok, string.Join("; ", changed.Errors.Select(e => e.Message)));
                var edited = document.Serialize().Single().Content.ToArray();
                using var reopened = (GearboxDocument)plugin.Open(GearboxTestData.Overlay(file, edited));
                _ = reopened.BuildView();
                _ = reopened.ExpandNode(field.ValueId.StartsWith("sdu/", StringComparison.Ordinal) ? "sdus" : "difficulty");
                Assert.Equal(next, reopened.GetValue(field.ValueId).AsInt64());
                var before = GearboxContainer.Parse(original, title, document.Profile).Decode();
                var list = field.ValueId.StartsWith("sdu/", StringComparison.Ordinal) ? document.Profile ? "profile_sdu_list" : "sdu_list" : "game_state_save_data_for_playthrough";
                SerialWriteTests.AssertUnrelated(before, reopened.Root, list);
                Assert.Equal(Confidence.Inferred, field.Confidence);
                Assert.Contains("Not validated on console", field.Description);
                Assert.True(session.Undo().Ok);
                Assert.Equal(original, document.Serialize().Single().Content.ToArray());
                Assert.True(session.Redo().Ok);
                Assert.Equal(edited, document.Serialize().Single().Content.ToArray());
                Assert.True(session.Undo().Ok);
                Assert.False(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement("-1"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
                Assert.False(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement((maximum + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
                tested.Add(list);
            }
        }
        Assert.Contains("sdu_list", tested);
        Assert.Contains("game_state_save_data_for_playthrough", tested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MythArrayWrites_PreserveOtherPackedUnpackedAndNoncanonicalRecords(bool packed)
    {
        // Repeated field 3: deliberately noncanonical first value 1, selected second value 2,
        // then another occurrence and an unknown field. Only the selected varint may change.
        byte[] wire = packed ? [0x1A, 0x04, 0x81, 0x00, 0x02, 0x03, 0x18, 0x04, 0xF8, 0x07, 0x01]
            : [0x18, 0x81, 0x00, 0x18, 0x02, 0x18, 0x03, 0x18, 0x04, 0xF8, 0x07, 0x01];
        var message = ProtoMessage.Parse(wire, GearboxSchema.Tina, "PlayerPrestigeProfileData");
        ProtoArrayEdits.Set(message, "points_spent_by_index_order", 1, 7);
        Assert.Equal([1, 7, 3, 4], GearboxAccountViews.Integers(message, "points_spent_by_index_order").ToArray());
        var bytes = message.Encode();
        var diff = ByteDiffer.Compare(wire, bytes, options: new() { MergeGap = 0, ContextBytes = 0, MaxPreviewBytes = 0 });
        Assert.Equal(1, diff.ChangedBytes);
        Assert.Equal(4, Assert.Single(diff.Ranges).Offset);
        Assert.Throws<SaveFormatException>(() => ProtoArrayEdits.Set(message, "points_spent_by_index_order", 9, 0));
        Assert.Throws<SaveFormatException>(() => ProtoArrayEdits.Set(message, "points_spent_by_index_order", 0, -1));
        Assert.Equal(bytes, message.Encode());
        ProtoArrayEdits.Set(message, "points_spent_by_index_order", 1, 2);
        Assert.Equal(wire, message.Encode());
    }

    [Fact]
    public void WonderlandsRepeatedLevelChanges_DoNotMultiplyAwardedPointsAndUndoRestoresPolicy()
    {
        var plugin = new GearboxPlugin();
        var file = GearboxTestData.PrimaryCharacter("PPSA01493");
        using var document = (GearboxDocument)plugin.Open(file);
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new("character/level", FieldValue.Of(20))]).Succeeded);
        _ = document.BuildView();
        var awarded = document.GetValue("character/skill-points");
        var checkpoint = document.CaptureState();
        Assert.True(document.ApplyEdits([new("character/level", FieldValue.Of(12))]).Succeeded);
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new("character/level", FieldValue.Of(20))]).Succeeded);
        _ = document.BuildView();
        Assert.Equal(awarded, document.GetValue("character/skill-points"));
        _ = document.RestoreMemento(checkpoint);
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new("character/level", FieldValue.Of(21))]).Succeeded);
        _ = document.BuildView();
        Assert.Equal(awarded.AsInt64() + 1, document.GetValue("character/skill-points").AsInt64());
    }
}
