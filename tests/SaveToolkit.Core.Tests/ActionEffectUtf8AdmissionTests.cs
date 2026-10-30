using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectUtf8AdmissionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OversizedLegacyIdentityLabelOrValueRejectsBeforeFollowingRow(int location)
    {
        var oversized = new string('x', ActionPlanSnapshots.MaxSerializedBytes + 1);
        var first = new PlannedChange(location == 0 ? oversized : "legacy",
            location == 1 ? oversized : "Legacy", FieldValue.Of("old"),
            FieldValue.Of(location == 2 ? oversized : "new"));
        var rows = new VisitedList<PlannedChange>([first,
            new("next", "Next", FieldValue.Of(1), FieldValue.Of(2))]);

        var error = Assert.Throws<InvalidOperationException>(() =>
            ActionPlanSnapshots.Snapshot(new ActionPlan { Changes = rows }));

        Assert.Contains("bounded budget", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, rows.Visits);
        Assert.Equal(0, rows.IndexReads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ExactWireAllowancePreservesUtf8ReplacementAndJsonEscaping(int sample)
    {
        var text = sample switch
        {
            0 => "ASCII",
            1 => "é中",
            2 => "\U0001F600",
            3 => "a\uD800b", // Replacement fallback for an unpaired high surrogate.
            4 => "a\uDC00b", // Replacement fallback for an unpaired low surrogate.
            _ => "\"\\\n\u0000",
        };
        var plan = new ActionPlan { Warnings = [text] };
        var exactBytes = JsonSerializer.SerializeToUtf8Bytes(plan).Length;

        Assert.True(Encoding.UTF8.GetByteCount(text) >= text.Length);
        Assert.Equal(exactBytes, ActionPlanSnapshots.SerializedSize(plan, exactBytes));
        Assert.Equal(text, Assert.Single(ActionPlanSnapshots.Snapshot(plan, exactBytes).Warnings));
        var oneExtra = plan with { Warnings = [text + "x"] };
        Assert.Equal(exactBytes + 1, JsonSerializer.SerializeToUtf8Bytes(oneExtra).Length);
        Assert.Throws<InvalidOperationException>(() => ActionPlanSnapshots.Snapshot(oneExtra, exactBytes));
    }

    [Fact]
    public void Utf8ReplacementLowerBoundHoldsForEverySingleCodeUnitAndSurrogatePairExtremes()
    {
        // This finite runtime check supports the length precheck's lower-bound argument;
        // exact JSON wire admission remains separately tested, not inferred from Length.
        Span<char> single = stackalloc char[1];
        for (var unit = char.MinValue; ; unit++)
        {
            single[0] = unit;
            Assert.True(Encoding.UTF8.GetByteCount(single) >= single.Length);
            if (unit == char.MaxValue) break;
        }
        Assert.Equal(4, Encoding.UTF8.GetByteCount("\uD800\uDC00"));
        Assert.Equal(4, Encoding.UTF8.GetByteCount("\uDBFF\uDFFF"));
    }
}
