using System.Text.Json;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class PresetCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledReplanRestoresPreviousTokenAndCompleteSessionState(bool atFinalPublication)
    {
        using var cancellation = new CancellationTokenSource();
        var document = new ControlledPresetDocument();
        using var fixture = new MutationSessionFixture(document);
        var view = JsonSerializer.Serialize(fixture.Session.BuildView());
        var preset = ControlledPresetDocument.Preset();
        var previous = fixture.Session.PlanPreset(preset);
        Assert.NotNull(previous.PlanId);
        var before = fixture.Session.State;
        var bytes = document.Serialize()[0].Content.ToArray();
        if (atFinalPublication)
        {
            // PlanPresetCore finishes serialization and publishes B before RunPreflight's
            // final cancellation check. A must be restored rather than leaving unreturned B.
            document.OnPlan = cancellation.Cancel;
        }
        else
        {
            document.OnResolve = (targets, choices) =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
                return document.Resolve(targets, choices);
            };
        }

        Assert.Throws<OperationCanceledException>(() => fixture.Session.PlanPreset(preset,
            cancellationToken: cancellation.Token));

        Assert.Equal(before, fixture.Session.State);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        Assert.Equal(view, JsonSerializer.Serialize(fixture.Session.BuildView()));
        Assert.Equal(0, document.ApplyCalls);
        document.OnResolve = null;
        document.OnPlan = null;
        Assert.True(fixture.Session.ApplyPresetPlan(previous.PlanId!, previous.Revision).Ok);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(0, document.Value);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledApplyPreflightDoesNotConsumeToken(bool duringReplan)
    {
        using var cancellation = new CancellationTokenSource();
        var document = new ControlledPresetDocument();
        using var fixture = new MutationSessionFixture(document);
        var view = JsonSerializer.Serialize(fixture.Session.BuildView());
        var preview = fixture.Session.PlanPreset(ControlledPresetDocument.Preset());
        var before = fixture.Session.State;
        if (duringReplan) document.OnPlan = cancellation.Cancel;
        else document.OnResolve = (targets, choices) =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return document.Resolve(targets, choices);
        };

        Assert.Throws<OperationCanceledException>(() => fixture.Session.ApplyPresetPlan(
            preview.PlanId!, preview.Revision, cancellation.Token));

        Assert.Equal(before, fixture.Session.State);
        Assert.Equal(0, document.Value);
        Assert.Equal(new byte[] { 0 }, document.Serialize()[0].Content.ToArray());
        Assert.Equal(view, JsonSerializer.Serialize(fixture.Session.BuildView()));
        Assert.Equal(0, document.ApplyCalls);
        document.OnResolve = null;
        document.OnPlan = null;
        Assert.True(fixture.Session.ApplyPresetPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(1, document.ApplyCalls);
    }

    [Fact]
    public void CancellationAfterAtomicApplyBeginsCompletesHistoryAndConsumesToken()
    {
        using var cancellation = new CancellationTokenSource();
        var document = new ControlledPresetDocument { OnApply = cancellation.Cancel };
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();
        var preview = fixture.Session.PlanPreset(ControlledPresetDocument.Preset());

        var result = fixture.Session.ApplyPresetPlan(preview.PlanId!, preview.Revision, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.Ok);
        Assert.Equal(9, document.Value);
        Assert.Equal(preview.Revision + 1, result.State.Revision);
        Assert.True(result.State.CanUndo);
        Assert.False(fixture.Session.ApplyPresetPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(1, document.ApplyCalls);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(0, document.Value);
        Assert.False(fixture.Session.State.CanUndo);
    }
}
