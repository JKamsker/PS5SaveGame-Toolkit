using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class PlayTimeWriteTests
{
    [Theory]
    [InlineData("PPSA01911", "ff7remake000")]
    [InlineData("PPSA01911", "ff7remakeplus000")]
    [InlineData("PPSA08664", "ff7rebirth000")]
    [InlineData("PPSA18936", "ff7rebirth000")]
    public void StoredSecondsUseImmutableOpeningBoundAndOnlyChangeFirstRecordWord(string title, string name)
    {
        using var fixture = new CoverageFixture(title, name);
        var tabs = Assert.IsType<TabsNode>(fixture.Document.BuildView());
        var overview = tabs.Tabs.Single(tab => tab.Title == "Overview");
        var field = Assert.Single(Assert.Single(overview.Children.OfType<SectionNode>()).Children.OfType<FieldNode>(), field => field.ValueId == "game/playtime");
        var spec = Assert.IsType<IntegerSpec>(field.Spec);
        Assert.False(spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, field.Confidence);
        Assert.StartsWith(WriteWarnings.Experimental, field.Warning);
        Assert.Equal(IntegerFormat.Duration, spec.Format);
        Assert.Equal(1, spec.UnitsPerSecond);
        var original = field.Value.AsUInt32();
        Assert.Equal((Int128)original, spec.Max);
        Assert.False(fixture.Document.ApplyEdits([new("game/playtime", FieldValue.Of((long)original + 1))]).Succeeded);
        Assert.False(fixture.Document.ApplyEdits([new("game/playtime", FieldValue.Of(-1L))]).Succeeded);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.True(fixture.Session.ApplyEdits([new("game/playtime", JsonSerializer.SerializeToElement(0))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var edited = fixture.Bytes();
        ByteChangeAssertions.OnlyOffsetsChanged(fixture.Original, edited, Enumerable.Range(GameplayLayout.For(fixture.Schema).PlayTimeOffset, sizeof(uint)));
        _ = fixture.Session.BuildView();
        Assert.True(fixture.Session.ApplyEdits([new("game/playtime", JsonSerializer.SerializeToElement(original))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(edited, fixture.Bytes());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(edited, fixture.Bytes());
        using var reopened = fixture.Plugin.Open(SaveToolkit.Testing.MemorySaveSet.SingleFile(name, edited, title));
        _ = reopened.BuildView();
        Assert.Equal(0U, reopened.GetValue("game/playtime").AsUInt32());
        Assert.Equal(edited, Assert.Single(reopened.Serialize()).Content.ToArray());
        Assert.Equal(LinkedRecords.Parse(fixture.Original, fixture.Schema, false), LinkedRecords.Parse(edited, fixture.Schema, false));
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(uint.MaxValue)]
    public void ExtremeOpeningTimesStayLosslessAndDoNotBecomeAnUnboundedNewRange(uint seconds)
    {
        const string title = "PPSA08664";
        var schema = Ff7Schema.ForTitle(title)!;
        var bytes = SyntheticGameplay.Create(title).Bytes;
        CoverageFixture.Write(bytes, GameplayLayout.For(schema).PlayTimeOffset, seconds);
        using var document = new FinalFantasy7Plugin().Open(SaveToolkit.Testing.MemorySaveSet.SingleFile("ff7rebirth000", bytes, title));
        _ = document.BuildView();
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(document.ApplyEdits([new("game/playtime", FieldValue.Of((long)seconds + 1))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new("game/playtime", FieldValue.Of(0L))]).Succeeded);
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new("game/playtime", FieldValue.Of((long)seconds))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
