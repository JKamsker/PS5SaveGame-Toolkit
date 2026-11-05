using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class HistoryLimitNoticeTests
{
    [Fact]
    public void ExceedingEntryLimitWarnsWhenEarlierChangesBecomeIrreversible()
    {
        var document = new ScalarBatchDocument(1);
        using var fixture = new MutationSessionFixture(document,
            new DocumentSessionOptions { MaxHistory = 1 }, () => new ScalarBatchDocument(1));
        var session = fixture.Session;
        _ = session.BuildView();
        var first = session.ApplyEdits([Edit(1)]);
        Assert.True(first.Ok);
        Assert.Empty(first.Warnings);

        var second = session.ApplyEdits([Edit(2)]);

        Assert.True(second.Ok);
        Assert.Contains(second.Warnings, warning => warning.Contains("history", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("limit of 1", StringComparison.Ordinal)
            && warning.Contains("undone", StringComparison.Ordinal));
        Assert.True(session.Undo().Ok);
        Assert.Equal(FieldValue.Of(1), document.GetValue(ScalarBatchDocument.Id(0)));
        Assert.False(session.State.CanUndo);
        Assert.True(session.State.IsDirty);
        Assert.True(session.Redo().Ok);
        Assert.Equal(FieldValue.Of(2), document.GetValue(ScalarBatchDocument.Id(0)));
    }

    private static IncomingEdit Edit(int value) => new(ScalarBatchDocument.Id(0), JsonSerializer.SerializeToElement(value));
}
