using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaTimerPreservationTests
{
    private const string TimerId = "progress/play-seconds";
    private const int TimerRelativeOffset = 231;
    private const int NegativeZeroBits = int.MinValue;

    [Theory]
    [InlineData(0)]
    [InlineData(NegativeZeroBits)]
    public void AssigningNumericZeroPreservesTheImportedZeroEncoding(int originalBits)
    {
        var original = WithTimerBits(originalBits);
        using var document = Open(WithBytes(original));
        var result = document.ApplyEdits([new(TimerId, FieldValue.Of(0.0))]);

        Assert.True(result.Succeeded);
        Assert.Equal(0, document.GetValue(TimerId).AsDouble());
        Assert.DoesNotContain(result.Changes, change => change.OldValue != change.NewValue);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(NegativeZeroBits)]
    public void ReturningToZeroAndCoreHistoryRestoreExactTimerAndChecksum(int originalBits)
    {
        var original = WithTimerBits(originalBits);
        var file = Assert.IsType<DiskSaveFile>(Slot("suckerpunchUser1"));
        var plugin = new SuckerPunchPlugin();
        using var document = Open(WithBytes(original));
        using var session = new DocumentSession("timer-zero", file, plugin, document,
            openOriginal: () => Open(WithBytes(original)));
        session.BuildView();

        Assert.True(session.ApplyEdits([Incoming(123.25)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var changed = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.NotEqual(original, changed);
        Assert.True(session.ApplyEdits([Incoming(0)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Empty(session.GetChanges(cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(changed, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(changed, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static IncomingEdit Incoming(double seconds) => new(TimerId, JsonSerializer.SerializeToElement(seconds));

    private static byte[] WithTimerBits(int bits)
    {
        var original = Slot("suckerpunchUser1").ReadAllBytes();
        ReadOnlySpan<byte> table = "\x04\0\0\0Easy"u8;
        var position = 0;
        while (position < original.Length && original.AsSpan(position).IndexOf(table) is var relative && relative >= 0)
        {
            var start = position + relative;
            if (original.AsSpan(start + table.Length + sizeof(float)).StartsWith("\x06\0\0\0Medium"u8))
            {
                BinaryPrimitives.WriteInt32LittleEndian(original.AsSpan(start + TimerRelativeOffset), bits);
                RepairChecksum(original);
                return original;
            }
            position = start + table.Length;
        }
        throw new InvalidOperationException("Missing difficulty counter sequence.");
    }
}
