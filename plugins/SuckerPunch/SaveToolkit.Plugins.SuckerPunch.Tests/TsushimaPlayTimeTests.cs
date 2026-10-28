using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaPlayTimeTests
{
    private const string TimerId = "progress/play-seconds";
    private const int TimerRelativeOffset = 231;
    private const double ObservedMaximum = 198_318.828125;

    [Fact]
    public void EveryObservedTimerEditsReopensAndChangesOnlyFloatAndChecksum()
    {
        var plugin = new SuckerPunchPlugin();
        var files = SampleSaves.Load(TsushimaTitle).AllDiskFiles.Where(file => plugin.Probe(file).IsMatch).ToArray();
        Assert.Equal(25, files.Length);
        var editedCount = 0;
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            using var document = Open(file);
            if (Word(original, 12) == 47)
            {
                Assert.False(document.ApplyEdits([new(TimerId, FieldValue.Of(60.0))]).Succeeded);
                Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
                continue;
            }

            var field = Timer(document);
            Assert.False(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Contains("Not validated on console", field.Description);
            Assert.Contains("param.sfo SUBTITLE remain unchanged", field.Warning);
            var spec = Assert.IsType<FloatSpec>(field.Spec);
            Assert.Equal(ObservedMaximum, spec.Max);
            var before = field.Value;
            const float target = 123.25f;
            var result = document.ApplyEdits([new(TimerId, FieldValue.Of(target))]);
            Assert.True(result.Succeeded);
            Assert.Contains(result.Warnings, warning => warning.Contains("Not validated on console", StringComparison.Ordinal));
            Assert.Equal(target, document.GetValue(TimerId).AsDouble());
            var expected = original.ToArray();
            var offset = TimerOffset(original);
            BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(offset), target);
            RepairChecksum(expected);
            var output = Assert.Single(document.Serialize());
            Assert.Equal(file.RelativePath, output.RelativePath);
            var edited = output.Content.ToArray();
            Assert.Equal(expected, edited);
            WriteAssertions.OnlyRangesChanged(original, edited, [(8, sizeof(uint)), (offset, sizeof(float))]);
            using var reopened = Open(WithBytes(edited));
            Assert.Equal(target, reopened.GetValue(TimerId).AsDouble());
            Assert.True(document.ApplyEdits([new(TimerId, before)]).Succeeded);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            editedCount++;
        }

        Assert.Equal(23, editedCount); // The v47 memory snapshot has a primary and backup entry.
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(198_319.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    public void InvalidTimersRejectTheWholeBatch(double value)
    {
        var file = Slot("suckerpunchUser1");
        using var document = Open(file);
        var outcome = document.ApplyEdits([Edit(SuppliesId, 86), new(TimerId, FieldValue.Of(value))]);
        Assert.False(outcome.Succeeded);
        Assert.Empty(outcome.Changes);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void WrongTypesRejectAndZeroAndMaximumReopen()
    {
        var file = Slot("suckerpunchUser1");
        using var document = Open(file);
        FieldValue[] invalid = [FieldValue.Null, FieldValue.Of("60"), FieldValue.Of(true), FieldValue.Of(Int128.MaxValue)];
        foreach (var value in invalid)
        {
            Assert.False(document.ApplyEdits([new(TimerId, value)]).Succeeded);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }

        foreach (var value in new[] { 0.0, ObservedMaximum })
        {
            Assert.True(document.ApplyEdits([new(TimerId, FieldValue.Of(value))]).Succeeded);
            using var reopened = Open(WithBytes(Assert.Single(document.Serialize()).Content.ToArray()));
            Assert.Equal(value, reopened.GetValue(TimerId).AsDouble());
        }
    }

    [Fact]
    public void CoreUndoesAndRedoesTimerAndQuantityAsOneBatch()
    {
        var file = Assert.IsType<DiskSaveFile>(Slot("suckerpunchUser1"));
        var plugin = new SuckerPunchPlugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("timer", file, plugin, document);
        _ = session.BuildView();
        var original = file.ReadAllBytes();
        Assert.True(session.ApplyEdits([new IncomingEdit(TimerId, JsonSerializer.SerializeToElement(123.25)),
            new IncomingEdit(SuppliesId, JsonSerializer.SerializeToElement("86"))]).Ok);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Empty(session.GetChanges().Changes);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void OriginalAboveObservedMaximumCanBeRestoredAndPaddingIsPreserved()
    {
        var bytes = Slot("suckerpunchUser1").ReadAllBytes();
        const float originalSeconds = 300_000f;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(TimerOffset(bytes)), originalSeconds);
        bytes[^1] = 0xA5;
        RepairChecksum(bytes);
        using var document = Open(WithBytes(bytes));
        Assert.Equal(originalSeconds, Assert.IsType<FloatSpec>(Timer(document).Spec).Max);
        Assert.True(document.ApplyEdits([new(TimerId, FieldValue.Of(0.0))]).Succeeded);
        Assert.True(document.ApplyEdits([new(TimerId, FieldValue.Of(originalSeconds))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void UnobservedVersionsExposeNoTimerWriter()
    {
        var bytes = Slot("suckerpunchUser1").ReadAllBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 50);
        RepairChecksum(bytes);
        using var document = Open(WithBytes(bytes));
        Assert.False(document.ApplyEdits([new(TimerId, FieldValue.Of(60.0))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static FieldNode Timer(ISaveDocument document) => Assert.IsType<TabsNode>(document.BuildView()).Tabs[0].Children
        .OfType<FieldNode>().Single(field => field.ValueId == TimerId);

    private static int TimerOffset(byte[] bytes)
    {
        ReadOnlySpan<byte> easy = "\x04\0\0\0Easy"u8;
        var start = 0;
        while (start < bytes.Length && bytes.AsSpan(start).IndexOf(easy) is var relative && relative >= 0)
        {
            var table = start + relative;
            if (bytes.AsSpan(table + easy.Length + sizeof(float)).StartsWith("\x06\0\0\0Medium"u8))
            {
                return table + TimerRelativeOffset;
            }
            start = table + easy.Length;
        }
        throw new InvalidOperationException("Missing difficulty counter sequence.");
    }
}
